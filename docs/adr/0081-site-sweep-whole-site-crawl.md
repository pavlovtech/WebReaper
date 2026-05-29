# 0081. Site sweep: whole-site recursive crawl as a fourth Crawl outcome arm

**Status:** Accepted (design pass 2026-05-29). Designed, not yet built; targets a 10.2.0 minor.
**Date:** 2026-05-29
**Deciders:** Alex (HITL), Claude (design pass)

## Context

WebReaper can scrape one page (`webreaper scrape <url>`) or follow one hop (`--follow <selector>`), but it cannot crawl an entire site in one operation. The only way to cover a whole site today is `map` (URL discovery) piped into a shell loop that scrapes each URL:

```bash
webreaper map https://example.com > urls.txt
while read -r u; do webreaper scrape "$u"; done < urls.txt > pages.jsonl
```

That is not user-friendly for a tool that calls itself a crawler, and Firecrawl ships a one-shot `crawl`. The gap was raised directly by the owner.

Three facts shape the design:

1. **The crawl engine is a finite Selector chain.** ADR-0001 made the **Crawl outcome** a closed sum of three arms (`Parsed | Followed | Paginated`); ADR-0030 gave `LinkPathSelector` its construction grammar and two named factories (`Follow`, `Paginate`). `Follow` is exactly one hop by construction: a **Transit page**'s children **Advance** (dequeue the head selector), so depth is fixed by the chain length. There is no recursive on-domain follow primitive.

2. **The Site mapper is not a crawl.** ADR-0042's `SiteMapper` discovers URLs in one HTTP pass (robots.txt + sitemap.xml, one level of index recursion, plus root-page `<a href>` links). With no sitemap it sees only the root page's anchors, so map-then-scrape silently misses deep pages. Its glossary entry explicitly says "Discovery is one HTTP request, not a Crawl."

3. **The termination machinery a recursive crawl needs already exists.** The **Visited-link tracker** is the atomic idempotency authority that dedups discovered children before the **Outstanding-work latch** counts them, and the **Stop rule**'s page-limit cutoff bounds the run. **Pagination already Retains** (its next-page children keep the selector instead of advancing). So the one missing primitive is a follow whose children Retain it and whose page is *also* extracted.

ADR-0001 anticipated this exact situation: "if a fourth page category ever appears, adding one arm to the closed union plus one shell branch is a small, local change; that future cost is cheaper than carrying the framework now." It rejected an open *strategy framework*, not a fourth arm.

## Decision

Add a whole-site recursive crawl, the **Site sweep**, as the fourth **Page category**. The user-facing verb is `crawl` (Firecrawl parity, discoverable); the mechanism is named **Sweep** so it never collides with the heavily-used Crawl / Crawl step / Crawl outcome / Crawl driver family. Both `scrape` and `crawl` produce a Crawl in the glossary sense; the verbs name the *scope*, not a different engine.

Eight parts:

1. **A fourth `CrawlOutcome` arm, `Swept`, single-pass.** A **Sweep page** is both extracted and followed-from, which breaks the current "extract XOR follow" dichotomy. The `Swept` arm carries this page's `ParsedData` *and* its on-domain child Jobs. The **Job report** already carries an optional `ParsedData` plus children, so its shape is unchanged; the invariant "ParsedData present iff a Target page" widens to "iff a Target page or a Sweep page." This is the local arm-plus-branch addition ADR-0001 designed for.

2. **A new `LinkPathSelector.Sweep(...)` factory**, the third intent-shape beside `Follow` and `Paginate` (the ADR-0030 pattern). It marks the selector recursive; the **Crawl step**, seeing a recursive head selector, returns the `Swept` arm and produces child Jobs that **Retain** the sweep selector, so the traversal perpetuates until the frontier saturates. The default link selector is `a[href]`; an optional selector restricts which links the sweep follows (for example `a[href^='/blog/']`).

3. **On-domain by default, dependency-free.** The sweep follows only links whose host equals the start host, treating a leading `www.` as equal to the apex. `--include-subdomains` broadens to a suffix match on the apex host, documented as a heuristic, not public-suffix-list-correct. Registrable-domain matching by default was rejected because it needs a public-suffix-list dependency, against the ADR-0009 dependency-light-core bias.

4. **Bounds reuse the Stop rule.** `--max-pages` (default 1000, matching the Site mapper's `MaxUrls`) maps onto the existing page-limit **cutoff**, so no new stop mechanism. `--max-depth` (default unlimited) reads the Job's parent-backlink-chain length, which the Job already carries, so it adds no new Job state.

5. **Per-page extraction is the existing Content extractor.** Markdown `{ title, markdown }` by default (the `AsMarkdown()` terminal), `--schema` for the deterministic **Schema fold**. One schema is applied to every Sweep page; a non-matching page yields a sparse record under the fold's ADR-0029 swallow-and-log policy, never aborting the sweep. Markdown-by-default sidesteps page heterogeneity entirely (every page becomes clean content), which is why it is the default rather than requiring a schema.

6. **Output is JSON Lines, streamed.** Each Sweep page emits to the **Sink**s as it is extracted (the existing fan-out), so records arrive incrementally rather than after the whole site finishes. The CLI writes one JSON object per line to stdout, or to `--output <file>`.

7. **Sitemap seeding, default-on.** The sweep seeds its frontier from the Site mapper's discovered URLs (when a sitemap exists) in addition to recursive link-following, so a site with a sitemap but sparse internal linking is still covered. `--no-sitemap` opts out (mirroring `map`). The two discovery modes union through the same Visited-link tracker, so seeds and followed links dedup against each other.

8. **Surface.** CLI: `webreaper crawl <url> [--schema <path>] [--max-pages <n>] [--max-depth <n>] [--include-subdomains] [--no-sitemap] [--output <path>]`. Library: `ScraperEngineBuilder.Crawl(url).AsMarkdown().Sweep(options?)` or `.Extract(schema).Sweep(options?)`, where `Sweep` appends the recursive selector to the chain (the chain-level sibling to `Follow` / `Paginate`). The CLI `crawl` command is sugar over this path.

The driver, latch, Stop rule, Visited-link tracker, and Sink fan-out are reused wholesale. Recursion terminates when the on-domain frontier saturates (no new URLs) or the page-cap cutoff trips.

## Considered options

- **Map-then-scrape composition (rejected).** Reuses existing pieces with zero engine change, but inherits the Site mapper's shallow discovery (sitemap plus root-page links only). A command named `crawl` returning a fraction of a sitemap-less site is the surprising behaviour that makes a crawler feel broken. Map-then-scrape is the right model for `map`, not for `crawl`.
- **Two-pass recursive-discover-then-extract (rejected).** Preserves the three-arm sum: a recursive pure-follow discovery crawl collects URLs, then each is extracted as a Target crawl, with the **Page cache** covering the re-fetch. But discovery-as-a-crawl has no natural URL output, it double-fetches without the cache (and couples fragilely to it with), and it cannot stream results.
- **Driver-mode link harvesting above the Crawl step (rejected).** Keeps three arms by harvesting links per page in the driver. But deciding what to follow *is* the Crawl step's job ("the shell reports, the driver decides"); harvesting in the driver fragments the core principle.
- **Recursive discovery inside the Site mapper (rejected).** Contradicts its glossary definition ("Discovery is one HTTP request, not a Crawl") and would spend visited-link, page-processor, and sink budgets a discovery operation does not need.
- **Registrable-domain (eTLD+1) on-domain default (rejected).** More complete (catches `blog.example.com` from `example.com`), but needs a public-suffix-list dependency in core, against the ADR-0009 bias. Offered instead as the `--include-subdomains` heuristic opt-in.

## Consequences

Good:
- One command, `webreaper crawl <url> > pages.jsonl`, covers a whole site to JSON. Firecrawl parity, no shell loop.
- The exact evolution ADR-0001 and ADR-0030 designed for: one closed-sum arm, one Crawl-step branch, one named selector factory. Not a new framework.
- Reuses the Visited-link tracker (dedup plus termination), the Outstanding-work latch, the Stop rule, and the Sink fan-out unchanged.
- Single-pass (one fetch per page) and streaming.

Costs:
- Touches the two most foundational crawl ADRs (0001, 0030), as their intended extension. `CrawlOutcome` grows from three arms to four; the Job-report invariant widens.
- The `LinkPathSelector` record gains a recursive marker, so the selector-chain JSON codec (`ImmutableQueueJsonConverters`) and the construction grammar (ADR-0030) gain one field to round-trip and guard.
- On-domain matching is a host-plus-`www` heuristic, not public-suffix-list-correct; cross-subdomain sites need `--include-subdomains`, itself a heuristic.

## Implementation sketch (not yet built)

- `WebReaper/Domain/Selectors/LinkPathSelector.cs`: a recursive marker (an init-only `bool` or equivalent) and a `Sweep(selector = "a[href]", …)` factory; extend the ADR-0030 construction guards to the new shape.
- The **Crawl outcome** type and the **Crawl step**: the `Swept` arm and the branch that returns it (extract this page plus on-domain children that Retain the sweep selector); the on-domain filter applied when producing those children.
- `WebReaper/Serialization/Converters/ImmutableQueueJsonConverters.cs`: round-trip the recursive marker.
- `ScraperEngineBuilder.Sweep(options?)` plus the `ConfigBuilder` delegation; sitemap seeding wired through the existing `MapAsync` path.
- `WebReaper.Cli/Commands/CrawlCommand.cs` plus `Help.cs` and the bundled `SKILL.md`; flags `--schema` / `--max-pages` / `--max-depth` / `--include-subdomains` / `--no-sitemap` / `--output`.
- Tests: the new arm and Crawl-step branch (offline), the `Sweep` factory grammar, on-domain filtering (host, `www`, subdomains), termination on a fixed multi-page fixture, the JSON-codec round-trip, and a CLI parse test in the `ScrapeContextTests` style.
- Semver: additive (new CLI verb, new library method, new selector factory, new closed-sum arm). A 10.2.0 minor.
