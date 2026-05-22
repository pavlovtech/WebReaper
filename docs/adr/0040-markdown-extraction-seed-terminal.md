# `.AsMarkdown()` — a second `ICrawlSeed` terminal; LLM-ready Markdown is the no-schema default for the AI-native funnel

## Status

**Accepted — implementation complete** (2026-05-23; landed on branch
`ai-native-wave` off `origin/master`). **Slice 1 of the AI-native wave**:
the post-ADR-0039 work the repositioning plan
([docs/REPOSITIONING-PLAN.md](../REPOSITIONING-PLAN.md)) calls Phase 2.
Breaking: `ICrawlSeed` (Tier-1 public) gains one method, and
`IContentExtractor.ExtractAsync`'s doc widens — both fold into the
unreleased 10.0.0 wave. Reaches the funnel; ships free, MIT.

## Context

The repositioning plan locks the wedge: "the smallest possible call
returns LLM-ready text; the schema/JSON path is the upgrade"
([REPOSITIONING-PLAN.md §1](../REPOSITIONING-PLAN.md)). The 2026-05-23
firecrawl research (`research/Reviving Web Scraping Library with AI.md`)
sharpens it — agents reach for firecrawl because **every endpoint
defaults to LLM-ready Markdown**; the schema path is composed on top via
`formats: ["json"]`. Today WebReaper's structural invariant is the
opposite: ADR-0025 made "build with no schema" *unrepresentable*. A
caller cannot ask for "just the cleaned text of one page" without first
constructing a `Schema`, even if the schema serves no purpose. The
schema-required gate is exactly the friction the wedge is named for.

Three observations grounded in the current code.

**The wiring downstream of the gate is already null-tolerant.**
`ScraperConfig.ParsingScheme` is `Schema?` ("null means no extraction" —
[ScraperConfig.cs:16](../../WebReaper/Domain/ScraperConfig.cs)).
`Spider.ParsingScheme` is `Schema?` ("null means no extraction" —
[Spider.cs:31](../../WebReaper/Core/Spider/Concrete/Spider.cs)).
`CrawlStep.StepAsync` accepts `Schema?` and passes it through unchanged
to `IContentExtractor.ExtractAsync`. The only place a null `Schema`
hard-throws today is the deterministic `SchemaFold<TNode>` — and that
throw is correct: a Schema-driven fold cannot proceed without a Schema.
The hard-no is *strategy-local*, not seam-wide.

**The gate is at one point: `ConfigBuilder.Build` uses `_schema!` and
`ICrawlSeed` exposes one terminal, `Extract(Schema)`.** Nothing else in
the pipeline assumes a non-null schema. ADR-0025's structural guarantee
is therefore stated *too narrowly*: the real invariant it protects is
"a Crawl declares its extraction strategy before `BuildAsync`," and the
only way to declare one today happens to be supplying a Schema. A second
declaration shape — *Markdown extraction* — preserves the invariant
under a clarified statement of it.

**`IContentExtractor` (ADR-0039) is the home.** Its prose already
foresaw this — "an LLM-backed extractor is a second adapter
implementing this interface directly rather than folding a Schema."
Markdown extraction is the *zeroth* such adapter: a strategy that
ignores the Schema and returns cleaned, structured Markdown of the
loaded page. Crucially, this adapter ships **without an LLM dependency**
— it is pure deterministic DOM-to-Markdown; the AI-native feel is the
*output format*, not an inference call. That makes it the cheapest,
highest-leverage change to make the funnel feel AI-native, and the
right Slice 1 before any LLM extractor lands (ADR-0044).

The firecrawl `formats: []` composable array — multiple output
representations per call — is a tempting v1 generalisation but is not
needed for the wedge: a Markdown crawl produces a Markdown `ParsedData`
and a Schema crawl produces a Schema `ParsedData`. The two shapes are
sibling terminals on `ICrawlSeed`, not a composable matrix. The matrix
shape can be reached later by an explicit composer if demand surfaces
(see Considered options (f)); shipping it now is speculative generality
and an ADR-0025 violation in a different direction (a Crawl that
declares no specific strategy).

## Decision

Four moves; ADR-0025's structural guarantee strengthens, the
`IContentExtractor` seam (ADR-0039) stays a seam, and the funnel gains
the one-liner the plan names.

### 1. `ICrawlSeed` gains a second terminal: `AsMarkdown()`

```csharp
public interface ICrawlSeed
{
    ScraperEngineBuilder Extract(Schema schema);
    ScraperEngineBuilder AsMarkdown();  // NEW
}
```

`AsMarkdown()` ([Builders/ICrawlSeed.cs](../../WebReaper/Builders/ICrawlSeed.cs))
returns the same configurable `ScraperEngineBuilder` `Extract` does,
with `ParsingScheme` left null and an in-place
`MarkdownContentExtractor` wired as the engine's content extractor.
Every free fluent method and every satellite extension stays available;
the only difference is the absence of a `Schema`. The seed's existence
no longer implies *Schema* — it implies *an extraction strategy*, with
two declarations to choose from.

### 2. `MarkdownContentExtractor` — the new adapter of `IContentExtractor`

A deterministic, AOT-clean, LLM-ready Markdown extractor in
[WebReaper/Core/Parser/Concrete/MarkdownContentExtractor.cs](../../WebReaper/Core/Parser/Concrete/MarkdownContentExtractor.cs).
The strategy:

1. **Parse with AngleSharp** — the same dependency the CSS/XPath
   backends already use; no new transitive deps.
2. **Identify main content** with a tag-based Readability heuristic:
   `<article>` → `<main>` → `[role=main]` → `<body>`. The first one
   present wins. Cheap (no scoring), explains itself, ~95th-percentile
   correct on modern editorial pages.
3. **Strip non-content** descendants before walking: `<script>`,
   `<style>`, `<noscript>`, `<template>`, `<nav>`, `<aside>`,
   `<footer>`, `<header>`, `<form>`, `<button>`, `<iframe>`,
   `<dialog>`, `[role=navigation]`, `[role=banner]`,
   `[role=contentinfo]`, `[aria-hidden=true]`, `[hidden]`.
4. **Walk the DOM and emit GFM-flavoured Markdown** — h1–h6, p, br, hr,
   strong/b, em/i, code, pre, ul/ol/li, blockquote, a, img, table.
   Everything else falls through to text content. List/quote nesting is
   tracked by depth; whitespace is collapsed (no more than two
   consecutive newlines) at the end.
5. **Project to `JsonObject`**:
   ```json
   { "title": "<h1 or <title>", "markdown": "..." }
   ```
   ADR-0031's URL-merge folds `"url"` in at `ParsedData` construction —
   no new merge.

The extractor accepts `schema` and **ignores it** (it has no place to
project it onto). Passing one is benign; it does not throw. This is
strategy-local — the deterministic Schema fold remains strict about its
required Schema (ADR-0039 §4); MarkdownContentExtractor is permissive
about its unused one.

Bounded scope for v1 (deferred, named):
- **Readability-style content scoring** (Mozilla algorithm — node
  scoring by text density, link density, class hints). ~1000 lines, a
  perceptible per-page cost, and not gating the wedge. Tag-based
  heuristic ships now; scoring lands behind a future
  `MarkdownOptions { ScoreMainContent = true }` knob if real pages
  surface where the heuristic misses.
- **Configurable options** (include-images, max-length, image-as-data-URI,
  link policy). Sensible defaults today (images included, links absolute,
  no length cap); `MarkdownOptions` lands when a second caller proves
  the shape — same discipline as ADR-0036's "shape from the second
  adapter, not for it."
- **Per-format composition** (firecrawl's `formats: ["markdown", "json"]`
  on one call). A Markdown crawl and a Schema crawl are sibling
  terminals; a request for *both* outputs from one engine is rare and
  reachable today by composing two `Subscribe` calls or running two
  engines. See Considered options (f).

### 3. `IContentExtractor` doc widens — strategy-local schema requirement

[IContentExtractor.cs](../../WebReaper/Core/Parser/Abstract/IContentExtractor.cs)'s
doc states the schema requirement is *strategy-local*: the
deterministic Schema fold throws on null (and documents the
`ArgumentNullException`), the Markdown extractor and any future
LLM extractor may accept null. The signature stays `Schema? schema` —
this is exactly the variation the nullability already advertised; the
prose was just narrower than the seam intended.

### 4. `ConfigBuilder.Build` accepts a null schema

[ConfigBuilder.cs](../../WebReaper/Builders/ConfigBuilder.cs):
`_schema!` becomes `_schema` (matching `ScraperConfig.ParsingScheme`'s
`Schema?`). Build's structural promise stays — start URLs and an
extraction strategy are present by construction; the strategy is now
{Schema-driven *or* Markdown}, declared via `Extract` or `AsMarkdown`
respectively.

### What this is NOT

- **Not a relaxation of ADR-0025.** A `ScraperEngineBuilder` is still
  unreachable without going through `Crawl(...)` and an
  `ICrawlSeed` terminal — `BuildAsync` cannot be reached without a
  declared extraction strategy. ADR-0025's structural promise is
  strengthened (stated correctly) rather than weakened.
- **Not a change to the deterministic Schema fold.** `SchemaFold<TNode>`
  is byte-identical; its `ArgumentNullException` on a null schema is
  the documented contract — strict by design.
- **Not a new `IPageLoader` or new sink.** The Markdown extractor runs
  inside `CrawlStep` exactly where `SchemaFold` runs today; the loader
  is HTTP (or the registered headless transport — `CrawlWithBrowser`
  composes); the sinks emit `ParsedData` unchanged.
- **Not an LLM call.** The output is LLM-ready; the extraction is not.
  The LLM extractor adapter is ADR-0044, also implementing
  `IContentExtractor`.
- **Not a `WithMarkdown()` post-build modifier.** `AsMarkdown()` is a
  seed terminal because it *chooses the extraction strategy* — the same
  semantic role `Extract(schema)` plays. A `WithMarkdown()` modifier
  late in the chain would let a caller reach a builder *before*
  declaring a strategy, exactly the failure mode ADR-0025 prevents.

## Considered options

### (a) Loosen `ICrawlSeed.Extract` to accept a nullable Schema — rejected

`Extract(Schema? schema)` reads honestly only if `null` *means* "no
extraction." It does not — it means "Markdown extraction." Overloading
`null` to carry a distinct strategy is exactly the shape ADR-0035 just
removed for `PageAction`. Two named terminals (`Extract` /
`AsMarkdown`) document the choice; a nullable parameter would not.

### (b) `Crawl(url).AsMarkdown()` skipping `ICrawlSeed` — rejected

A separate `CrawlAsMarkdown(params string[] urls)` static would
duplicate the staged-entry's URL validation, the
`CrawlWithBrowser(urls, actions)` variant, and the seed lattice. The
seed *is* the place where "what to extract" is declared; adding a
sibling pre-seed staticeats the staged-entry's clarity.

### (c) `AsMarkdown(MarkdownOptions options)` overload now — rejected (deferred)

There are no callers yet; an options bag shaped without a second use
site is the speculative-generality bug ADR-0036 explicitly names. A
parameterless terminal with sensible defaults ships now;
`MarkdownOptions` lands when a real second caller proves the shape.

### (d) Browser-mode Markdown via `CrawlWithBrowser(urls).AsMarkdown()` — accepted, free

`AsMarkdown()` lives on `ICrawlSeed`, and both `Crawl(...)` and
`CrawlWithBrowser(...)` return one. A headless-browser-rendered page
flowing into the Markdown extractor is free — the satellite contributes
the loader, the extractor sees the loaded HTML, the same Readability
heuristic runs. No new code for this combination; the test set covers
the static path and the design has no static-only assumptions.

### (e) Roll our own HTML→Markdown — accepted, vs a NuGet dep

`ReverseMarkdown` and `Html2Markdown` are the popular .NET options.
`ReverseMarkdown` uses reflection-heavy attribute scanning (AOT-hostile
under the `IL2026`/`IL3050` discipline the AotSmokeTest enforces);
`Html2Markdown` is maintenance-quiet. Both add a transitive
dependency for ~150 lines of straightforward DOM walking we already
have AngleSharp for. The roll-our-own variant is AOT-clean by
construction, has no transitive risk, and reads as straight-line
imperative code a contributor can audit. The cost — slightly less
exhaustive HTML coverage than ReverseMarkdown — is paid by the
heuristic-driven content-area selection, which strips most of the
problematic shapes (forms, tables-as-layout, JS widgets) before the
walker ever sees them.

### (f) firecrawl-style `formats: []` composable matrix in v1 — rejected (deferred)

Firecrawl's pattern of `formats: ["markdown", "json", "links"]` on one
call composes output shapes per-page. It is genuinely useful, and the
seam to express it exists (`IContentExtractor` could be a list, the
`ParsedData` could carry a multi-format payload). But: (i) it
contradicts ADR-0025's "the strategy is declared once" — composition
would require *the seed* to carry multiple strategies, and the gate
loses its sharpness; (ii) every concrete v1 caller wants one format
(the CLI's `--as` flag, the agent skill's Markdown default); (iii) when
a caller actually needs two, today's path of two engines or two
`Subscribe`s works and is honest about its cost. A future
`AsMarkdownAndExtract(schema)` composer remains shapeable when a
concrete second-format caller arrives — same discipline as
ADR-0036/0039.

### (g) Mozilla-Readability port now — rejected (deferred)

The dmoz/readability algorithm (text density scoring, link density,
class hints, hierarchy) substantially outperforms tag-based heuristics
on noisy news/blog templates. It is also ~1000 lines, perceptibly
slower per page, and not gating the wedge ("does WebReaper feel
AI-native"). The tag heuristic ships now; a scoring backend can land
behind a `MarkdownOptions` knob when a real page surfaces where the
heuristic misses content the scorer would have found.

## Consequences

- **The funnel has its one-liner.** A caller writes
  ```csharp
  var engine = await ScraperEngineBuilder
      .Crawl("https://example.com")
      .AsMarkdown()
      .WriteToConsole()
      .BuildAsync();
  await engine.RunAsync();
  ```
  with no Schema construction — the smallest possible call returns
  LLM-ready Markdown. The CLI (ADR-0043) and Agent Skill build on it.
- **`IContentExtractor` has its second adapter.** ADR-0039 foretold
  this; the seam is now genuinely two-adapter. The LLM extractor
  (ADR-0044) is the third.
- **ADR-0025's promise is stated correctly.** "A Crawl declares its
  extraction strategy before `BuildAsync`" — the structural
  unrepresentability of "build with no strategy" stays. The seed
  terminal choice is now {Schema, Markdown}, extensible by future
  ADRs without further loosening.
- **Breaking change — `ICrawlSeed`.** Tier-1 public. A consumer
  implementing `ICrawlSeed` (none in this repo) breaks; consumers
  *calling* `Crawl(...).Extract(schema)` are unaffected. Folds into the
  unreleased 10.0.0 wave.
- **Breaking change — `IContentExtractor` doc.** The doc widens (the
  *behaviour* of the SchemaFold is unchanged); a consumer reading the
  ADR-0039 doc as "schema is universally required" must now read it as
  "the SchemaFold requires it." No code breaks.
- **`ConfigBuilder._schema!` becomes `_schema`.** A no-behaviour change
  in the Schema-driven path (the seed still calls `WithScheme`); a
  null `ParsingScheme` for the Markdown path was already supported by
  `Spider` / `ScraperConfig` / `CrawlStep`.
- **No new transitive dependency.** AngleSharp is already in the core.
- **AOT-clean.** The Markdown walker uses no reflection, no `dynamic`,
  no `Activator`, no `JsonSerializer.Serialize<T>` reflection paths —
  only `JsonValue.Create(string)` and `JsonObject` mutation. The
  `WebReaper.AotSmokeTest` gains a Markdown-extraction case.
- **CONTEXT.md** gains a **Markdown extraction** term, an `AsMarkdown`
  relationship line under the staged-builder entry, and a Flagged-
  ambiguities bullet for the strategy-local schema requirement.
- **CLAUDE.md** gains a one-line gotcha — "the Markdown extractor
  ignores the Schema; the deterministic fold doesn't."

## Implementation

Landed on `ai-native-wave`:

1. **`MarkdownContentExtractor.cs`** — new file in
   [WebReaper/Core/Parser/Concrete/](../../WebReaper/Core/Parser/Concrete/);
   public, AOT-clean, AngleSharp-DOM-driven.
2. **`ICrawlSeed.cs`** — `ScraperEngineBuilder AsMarkdown();` added;
   doc updated to name the strategy-choice lattice.
3. **`ScraperEngineBuilder.CrawlSeed` (private nested)** — `AsMarkdown`
   implementation; calls `SpiderBuilder.WithContentExtractor(new
   MarkdownContentExtractor())` and leaves `ConfigBuilder._schema`
   null.
4. **`ConfigBuilder.Build`** — `_schema!` → `_schema`.
5. **`IContentExtractor.cs`** — XML doc widened; the `ArgumentNullException`
   doc moved from seam-wide to SchemaFold-local.
6. **`MarkdownContentExtractorTests`** — new file in
   [WebReaper.Tests/WebReaper.UnitTests/](../../WebReaper.Tests/WebReaper.UnitTests/);
   covers main-content selection, the strip-list, headings, paragraphs,
   lists (ordered, unordered, nested), links, images, code (inline,
   block), blockquote, table (GFM), title selection (h1 vs `<title>`),
   whitespace normalisation, and the schema-ignored-not-thrown contract.
7. **`WebReaper.AotSmokeTest`** — adds a Markdown-extraction case
   asserting the AOT-published binary produces a valid `JsonObject`.
8. **`CONTEXT.md` / `CLAUDE.md` / `README.md`** — terms, relationship
   lines, the one-line gotcha, and a Markdown example in the README's
   "Getting started" section.

### Guardrails (run on the branch tip)

- `dotnet build WebReaper.sln` — 0 errors; warning count unchanged
  from the ADR-0039 baseline (no new warnings introduced by this
  ADR's added files).
- `dotnet test WebReaper.Tests/WebReaper.UnitTests` — all baseline
  tests pass; the new `MarkdownContentExtractorTests` add coverage.
- `WebReaper.AotSmokeTest` — `dotnet publish` (Native-AOT) emits no
  IL-trim warnings; the published native binary prints `AOT SMOKE: ALL
  PASS` including the new Markdown-extraction case.

## References

- ADR-0002 — the Schema fold and node-backend seam; this ADR adds a
  *second* strategy adapter on the same `IContentExtractor` seam, not a
  second fold.
- ADR-0025 — staged builder entry; this ADR widens the seed's
  terminal lattice from {Extract(Schema)} to {Extract(Schema),
  AsMarkdown()}, preserving the structural promise.
- ADR-0029 — coercion-failure policy; the Markdown extractor cannot
  hit Coerce (no Schema → no leaves), so the policy is structurally
  irrelevant to this strategy.
- ADR-0031 — `ParsedData` URL-merge; the Markdown payload gains the
  `"url"` key the same way every other extracted record does.
- ADR-0036 — link extraction is a function, not a seam; the "shape an
  interface *from* its second adapter, not *for* it" discipline cited
  here for `MarkdownOptions`, `formats: []`, and Readability scoring.
- ADR-0039 — `IContentExtractor` was renamed to anticipate this
  exact second adapter; this ADR cashes the cheque.
- REPOSITIONING-PLAN §1, §2.5 — the funnel's wedge ("the smallest
  possible call returns LLM-ready text"), and the CLI/Skill that
  consume this terminal in ADR-0043.
- LANGUAGE.md — "a seam waits for its second adapter." This is that
  second adapter.
