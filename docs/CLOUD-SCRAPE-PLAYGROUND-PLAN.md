# WebReaper Cloud Scrape Playground, Design + Build Plan

**Status:** Proposed, grilled and refined 2026-05-30. Architecture and all three owner calls resolved; ready to prototype (Phase 0). A live "try the real thing" playground on the marketing site that doubles as the first slice of WebReaper Cloud.
**Date:** 2026-05-30
**Owner:** Alex
**Decision trail:** the 2026-05-30 playground design grill against the domain model (ADR-0083, [CONTEXT.md](CONTEXT.md)). Two scope picks were made up front (any-URL, full pipeline) then *refined* by the grill: a canned landing hero plus two live tiers (the climb is gated, not ungated), a library-referencing backend (not a CLI shell-out), `ExtractWithPrompt` for Tier B, honest-loss UX, and resolved budget/gate/wallet calls. Sits under the open-core + managed-cloud strategy (this playground is WebReaper Cloud's first endpoint).

## One-line goal

Let a visitor paste a URL on the site and watch the real WebReaper pipeline scrape it live, the HTTP to browser to stealth escalation climb visible as it happens, then see clean Markdown / JSON out. The backend that serves this *is* the WebReaper Cloud scrape endpoint, so the marketing demo and the paid product are one system.

## What this commits you to (read this first)

The maximal pick (ungated + any URL + full browser/stealth/LLM) is not a demo, it is a public, unauthenticated, arbitrary-URL fetcher that drives a real browser and spends LLM tokens per request. That is two things at once:

1. **An SSRF-and-cost magnet.** Anyone can aim it at internal IPs or cloud metadata, and every run costs real money (Chromium CPU/RAM + LLM tokens).
2. **The seed of WebReaper Cloud.** The hardening you do here (isolation, egress control, rate limits, budget caps, gating) is exactly the managed product's free tier. Build it as a service, not a throwaway.

So: design-first, and do not ship the expensive path ungated.

## Prior art (what the market already does)

A live "paste a URL, see it scrape" playground is table stakes in this category. Not having one is a visible gap against the product the site positions against.

- **Firecrawl, [the bar to beat](https://www.firecrawl.dev/playground).** Paste any URL, runs the *same pipeline as the paid API*, returns Markdown / JSON / screenshot / metadata, scrape/crawl/map/extract modes, ungated (autoruns an example with no account). This is the direct competitor and exactly the ask.
- **Jina Reader, [the ungated-any-URL precedent](https://jina.ai/reader/).** Prefix `r.jina.ai/<url>` to get clean Markdown from any URL, free, no signup. Notably it is the *lightweight HTTP+Markdown* shape, and it controls cost purely with rate limits (20 req/min with no key, 500 with a free key). Proves the ungated model works, on the cheap path.
- **The full-browser crowd all gate it.** [Browserless](https://www.browserless.io/blog/rest-api-playground), [Zyte](https://www.zyte.com/blog/play-before-you-scrape-explore-zyte-api-settings-with-playground/), [Oxylabs](https://developers.oxylabs.io/scraping-solutions/web-scraper-api/web-scraper-api-playground), [Bright Data](https://docs.brightdata.com/scraping-automation/scraping-browser/features/playground), [Scrapeless](https://www.scrapeless.com/en/blog/scrapeless-scraping-browser-playground). All run real Chrome, all sit **behind a dashboard / account**.

**What the market teaches:** the ungated, instant ones lean on the cheap path + rate limits (Jina); the full-browser ones gate behind signup precisely because of the cost / abuse surface. Nobody gives away unauthenticated full-browser + stealth + LLM on arbitrary URLs with no gate. That is not a gap in the market, it is the market avoiding the expensive corner you picked.

**Where you would actually be novel:** no surveyed playground *visualizes the escalation climb* (HTTP -> 403 -> browser -> still blocked -> stealth -> success). That animation is your bot-bypass story made visible, and it is the one thing here that is not "Firecrawl but ours." Treat it as the headline, not the any-URL box.

## Pinned recommendation

**One canned hero + two live tiers.** The escalation climb (the differentiator) only happens when browser/stealth rungs are present (ADR-0083), so it cannot run on the cheap HTTP-only path. Rather than put the expensive path in front of anonymous traffic to show it off, the climb viz is a UI component fed by an event stream, and the front page feeds it a *recorded* stream.

- **Landing hero, the canned climb (ungated, zero per-visit cost).** A real climb against a known-hard target (Cloudflare/DataDome), captured once and replayed as the always-on front-page animation: HTTP -> 403 -> browser -> still blocked -> stealth -> success. This is the differentiator, on the front page, costing nothing per visit. The Phase 0 prototype ships as this hero.
- **Tier A, the live ungated taste.** Paste any URL, HTTP fetch to clean Markdown only. No headless browser, no LLM, no account. A few runs/day/IP behind a Turnstile challenge. Jina-parity: cheap, instant, safe.
- **Tier B, the live full pipeline (gated).** Browser + stealth escalation on any URL, plus an **optional plain-English extraction prompt** wired to `ExtractWithPrompt` (ADR-0084). Empty prompt -> climb -> clean Markdown (no LLM cost); filled prompt -> climb -> structured JSON via the LLM. Behind a lightweight email-capture gate, hard per-job caps, and a daily spend kill switch. This is where the climb runs *live* on the visitor's own blocked site and matches Firecrawl's "Extract" head-on. The WebReaper Cloud free tier seed.

This puts the wow on the front page where it sells, keeps anonymous arbitrary-URL traffic on the cheap HTTP path, and makes "watch it beat *your* Cloudflare site" the gated unlock. The climb viz component is built once and fed a recorded stream (hero) or a live stream (Tier B).

**Climb is already observable (verified in code):** `EscalatingPageLoader.LoadAsync` emits structured per-tier `ILogger` events (`"Loading {PageType} page {Url} at tier {Tier}"`, `"Page {Url} still blocked at the top tier: {Reason}"`, [EscalatingPageLoader.cs](../WebReaper/Core/Loaders/Concrete/EscalatingPageLoader.cs)). Streaming the climb needs a logger/sink that forwards these, not an invented progress protocol. Tier index -> friendly name (HTTP/browser/stealth) is a small mapping.

**Supporting decisions:**

- **Wallet:** you eat a *capped* cost on Tier B as marketing spend. Cheap model (Haiku) for demo extraction, hard token cap per run, daily dollar ceiling with a kill switch ("demo at capacity, sign up"). Offer BYO-key to lift the per-user limit (zero cost to you, converts power users).
- **Hosting:** Vercel stays UI + edge gate (Turnstile, rate limit, budget gate) only. The backend runs on **Fly Machines** (per-job microVMs, egress controllable), with Cloud Run jobs as the alternative. Vercel's serverless model cannot host the browser/stealth path (time caps, ephemeral FS, no persistent subprocess, ~220MB stealth download).
- **Cold-start vs warm pool:** a small **warm pool of 1 to 2 for Tier A** keeps the ungated front-page taste instant (the one place instant matters). **Tier B scales to zero** but streams a narrated "spinning up a clean browser..." event immediately, so the microVM cold boot + stealth launch reads as intentional, and the climb animation absorbs the rest (the wait is the show). Revisit a Tier B warm pool only if gated volume justifies the standing cost.
- **The escalation-climb visualization is the headline UX**, not an afterthought.

**Considered and rejected:** the raw pick, ungated full pipeline on any URL. Rejected because it is an open SSRF pivot and an unbounded LLM/compute bill exposed to anonymous traffic, and because no competitor does it, for those reasons. Tier B keeps the capability, behind a gate.

## The hard parts (non-negotiable for any public any-URL fetcher)

### 1. SSRF + hostile content (applies to BOTH tiers, Tier A still fetches arbitrary URLs)

App-level URL checks are not enough once a browser is driving. Enforce egress at the **network layer**:

- Per-job network namespace + egress proxy that blocks RFC1918, loopback, link-local (incl. `169.254.169.254` cloud metadata), and IPv6 equivalents.
- **DNS pinning:** resolve the hostname, validate the resolved IP against the blocklist, then connect to *that pinned IP*, to kill DNS-rebinding TOCTOU.
- Re-validate every redirect hop. Scheme allowlist: http/https only (no `file://`, `gopher://`, etc.).
- Each job runs in an **ephemeral sandbox** (one microVM/container per request, killed on timeout). You are executing attacker-supplied web content server-side; one job must not see another's data or escape.

### 2. Cost ceiling (the one that can actually hurt)

Hard per-job timeout (~45s), output size cap, per-IP daily cap, global concurrency cap, and a **daily dollar kill switch ($5/day to start)**. Tier B LLM cost bites **only when the user supplies an extraction prompt** (no prompt = browser/stealth + Markdown, zero LLM tokens); when present, Haiku + a token cap per run. When the daily budget is hit, flip the playground to a signup CTA.

### 3. Gating + abuse + legal

Turnstile on Tier A; **email capture** on Tier B (feeds the existing waitlist). Short result retention (TTL, not permanent). Abuse logging + rate limits. A ToS line covering "do not scrape what you are not allowed to," since a public scraper is a proxy for someone else's requests.

### 4. The losing case (residual block) is a first-class UX, not an error

Per ADR-0083 part 8, a page still blocked after the top tier is dropped (zero records) and tallied in `RunReport.BlockedPageCount`. Visitors *will* test the hardest sites they know, so the run that matters most is often a loss. Design for it:

- On a residual block, the climb viz shows the **real** outcome ("still blocked at stealth") then a non-embarrassing line tied to `BlockedPageCount`: "This site's protection beat our top tier. A captcha-solver tier is on the roadmap." ADR-0083's "blocked is data, not garbage" makes honesty consistent, not a patch. Never show a raw error or bare "0 records" on a block.
- **Narrative control:** the canned landing hero is a guaranteed win (a target you know you beat); live losses happen only in the **gated** Tier B, to a smaller already-converted audience, never on the front page. Accepted: a public any-URL box measures your real win-rate, and that exposure is the deliberate trade for the demo's credibility.
- **Deferred to v2:** turning a live loss into a lead ("want us to crack this? leave your email"). Honest-loss handling ships in v0; the lead-capture-on-loss touch does not.

## Architecture

```
[Vercel: playground UI]
   -> POST /scrape {url, tier}
   -> [edge: Turnstile + rate-limit + daily-budget gate]
   -> [backend on Fly Machines: a .NET worker (references the WebReaper library),
       one ephemeral isolated microVM per job]
        builds a ScraperEngine; egress locked at the network layer; DNS-pinned; killed on timeout
        Tier A: HTTP loader -> HtmlToMarkdown
        Tier B: escalating loader (HTTP -> browser -> stealth) + LLM/prompt extract
        climb streamed via an ILoggerProvider; records via a custom IScraperSink; RunReport for residual-block + budget
   -> stream events back (SSE) -> UI renders the live climb + Markdown/JSON
```

The backend references the WebReaper library directly (decided: not a shell-out to the CLI binary). SSRF defense is identical either way because egress is enforced at the microVM network layer, not in-process; the library path wins on streaming and builder control (the climb logger, the streaming sink, per-run token cap, BYO-key model wiring, prompt extraction), and it *is* the WebReaper Cloud product trajectory (a service on the library, not a CLI fork per request). Dogfood survives as "the same engine the CLI runs."

## Reused vs new

- **Reused (WebReaper library, referenced directly):** the ADR-0083 HTTP->browser->stealth escalating loader (the climb *is* the demo), `HtmlToMarkdown`, the LLM / prompt extractor (ADR-0084 `ExtractWithPrompt`, ADR-0067 `ExtractInferred`), `RunReport` telemetry (residual-block tally + cost), the `IScraperSink` and `ILoggerProvider` seams.
- **No CLI change needed.** The worker captures the climb by attaching an `ILoggerProvider` that forwards `EscalatingPageLoader`'s existing per-tier events, streams records via a custom `IScraperSink`, and reads `RunReport` directly. (The CLI's flag->tier composition logic is re-expressed in the worker; small, and a candidate for a shared helper.)
- **New (the service):** the .NET worker + its Fly Machines per-job microVM sandbox with locked egress; the SSRF egress proxy + DNS pinning; the Vercel edge gate (Turnstile + rate limit + budget); the playground UI with the SSE-driven escalation animation; the daily-budget kill switch.

## Build order (highest-risk-first, ship the safe real thing early)

0. **Build the climb-viz UI against a stubbed event stream.** Zero infra, zero cost, zero security surface. Validate the escalation-climb UX before any backend exists, then capture one real climb and ship this as the **canned landing hero** (the Q1 decision). Reused later for the live Tier B stream. (Cheap parallel win, start it now.)
1. **Tier A live.** Ungated HTTP->Markdown, any URL, on a small Fly container, behind Turnstile + per-IP rate limit + the SSRF egress guard. Ships the safe, real thing fast and matches Jina.
2. **Tier B live.** Full pipeline behind the signup gate + caps + daily kill switch, per-job microVM sandbox on Fly Machines. The Cloud free-tier seed.
3. **Graduate to WebReaper Cloud.** Accounts, API keys, billing, the managed product from the monetization plan. Tier B's design must not foreclose this.

## Owner calls (resolved 2026-05-30)

- **Daily budget ceiling: $5/day to start.** The kill switch flips the playground to a signup CTA when hit. Cheap to run given Haiku + LLM-only-on-prompt + caps; raise once real usage is visible.
- **Gate type: email capture.** Lowest friction, and it feeds the existing Stripe-ready waitlist. Real accounts arrive in Phase 3 (WebReaper Cloud).
- **Wallet: you eat the capped cost, BYO-key optional.** Marketing spend, bounded by the daily ceiling; power users paste their own LLM key to lift the per-user limit.

## Non-goals (v0)

- Accounts, API keys, billing (Phase 3).
- Crawl / map in the public playground. The playground's unit of work is a single **Target page** (the CLI `scrape` verb: one page load + extract), never a **Crawl** (the glossary's whole-site term, unbounded cost) or a **Site map**. "Scrape" throughout this doc means the single-URL verb, not the avoided synonym for Crawl.
- Custom browser actions / BYO-browser in the public playground.
- Long-term persistence of user scrape results (short TTL only).
