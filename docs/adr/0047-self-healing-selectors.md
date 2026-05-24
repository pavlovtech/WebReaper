# `SelfHealingContentExtractor` — LLM proposes selectors, the fold validates, the schema-cache persists the fix

## Status

**Accepted — implementation complete** (2026-05-23; landed on branch
`ai-native-wave` off `origin/master`). **Slice 8 of the AI-native wave**.
Per [REPOSITIONING-PLAN §2.2](../REPOSITIONING-PLAN.md): "LLM proposes
selectors → validate deterministically by re-running the fold against
the live document + typed schema → persist and demote the site back to
the deterministic path." Folds into the unreleased 10.0.0 wave; ships
free, MIT.

## Context

ADR-0046's `ExtractionRouter` solves "the deterministic path failed —
fall back to the LLM." That fallback is cheap on the first failed page
but it's *the same cost on every subsequent page* — every visit to the
broken site costs an LLM call.

The plan §2.2's self-healing answer: **when the deterministic path
fails, ask the LLM to propose new selectors, validate them by re-
running the deterministic fold with those selectors, and cache the
repaired schema** so future pages of the same crawl run deterministic
again. This is the firecrawl-research-digest insight #4: cached-
selector demotion ("LLM-as-proposer, not as-decider; demote the site
back to the deterministic path" — research/Reviving Web Scraping
Library with AI.md §AI-Driven Revolution).

### What "the schema cache" really is

The selectors live in a `Schema`. The cache stores a *patched Schema*
per crawl. On the first failed page:

1. Primary fold over the original Schema → invalid result.
2. Repairer (`ISelectorRepairer`) examines the document and the failed
   result, proposes a new Schema with patched selectors.
3. Primary fold over the *patched* Schema → if valid, cache it; the
   crawl uses the patch for every subsequent page.
4. If still invalid, no caching; the next page re-tries.

The cache lifetime is the in-process crawl by default. A satellite
adapter (e.g. a future `WebReaper.Redis` extension) ships persistent
caching the same way other distributed state ships.

### Cache key — the design call

`IContentExtractor.ExtractAsync(document, schema)` doesn't carry a
URL. The cache key options:

| Key | Captures | Risk |
|---|---|---|
| Schema reference identity | "Same Schema instance" | Misses across processes / serialise/deserialise roundtrips. |
| Schema hash (recursive) | "Same Schema shape" | One patch per shape — what we want for single-host crawls. |
| Schema hash + URL host | "Same Schema + same host" | Requires URL in the seam. |

v1 ships **schema reference identity** — the cleanest, no
serialisation cost, and the common case is one `Schema` instance per
crawl. Hash-based and per-host keying are clean v2 enhancements once
a real multi-host caller surfaces. This is the same discipline as the
page cache's `(url, pageType)` key (ADR-0041) but applied to a
different cache.

### The repairer protocol

`ISelectorRepairer` is a one-method interface. Given the original
Schema, the document the fold ran against, and the failed result, it
returns either a patched `Schema` or `null` (couldn't repair).

The default repairer in `WebReaper.AI` calls the LLM with:

```
System: You are repairing CSS selectors for a web scraper.
The original selectors no longer match. Given the failed result and
the page HTML, propose new CSS selectors for the fields with empty
values. Output a JSON object mapping field names to selector strings.

User:
Original schema: <field name + selector pairs>
Failed result: <JsonObject the deterministic fold produced>
Page (Markdown): <cleaned markdown>

Output JSON like: {"title": ".new-title-selector", "views": "span.views"}
```

The repairer parses the LLM's JSON, walks the original Schema, and
emits a copy with selectors swapped for the named fields. Fields not
mentioned by the LLM keep their original selectors.

### Validation discipline

The self-healing extractor only caches a patched Schema **after** the
fold's output passes the same validator the router uses
(SchemaSatisfiedValidator). The LLM proposes; the fold verifies;
neither is the sole decider. This is the
"LLM-Proposed Overrides with Local Validation" pattern (research
report).

## Decision

Three pieces — one new seam, one wrapper, one satellite adapter.

### 1. `ISelectorRepairer` — the repair seam

[WebReaper/Core/Parser/Abstract/ISelectorRepairer.cs](../../WebReaper/Core/Parser/Abstract/ISelectorRepairer.cs).
Public interface, one method:

```csharp
public interface ISelectorRepairer
{
    Task<Schema?> RepairAsync(
        Schema original,
        string document,
        JsonObject failedResult,
        CancellationToken cancellationToken = default);
}
```

A `null` return means "I tried, couldn't repair." A non-null `Schema`
is the proposed patch — the wrapper re-runs the fold with it.

### 2. `SelfHealingContentExtractor` — the wrapper

[WebReaper/Core/Parser/Concrete/SelfHealingContentExtractor.cs](../../WebReaper/Core/Parser/Concrete/SelfHealingContentExtractor.cs).
Public class implementing `IContentExtractor`. Constructor:

```csharp
public SelfHealingContentExtractor(
    IContentExtractor primary,
    ISelectorRepairer repairer,
    ILogger? logger = null)
```

`ExtractAsync`:

1. Look up a cached patched Schema for `schema` (reference identity).
2. Run `primary.ExtractAsync(document, cachedOrOriginal)`.
3. Validate with `SchemaSatisfiedValidator.IsSatisfied`.
4. If valid: return; cache the schema (if it was the original).
5. If invalid: `repairer.RepairAsync(...)`. If `null`, return the
   failed result. Otherwise, run primary with the patch, validate.
   If valid, cache the patch + return; else return the patched
   result (best-effort).

The cache is per-instance — one `SelfHealingContentExtractor` =
one crawl's cache. A second instance starts fresh.

### 3. `LlmSelectorRepairer` — the WebReaper.AI satellite implementation

[WebReaper.AI/LlmSelectorRepairer.cs](../../WebReaper.AI/LlmSelectorRepairer.cs).
Implements `ISelectorRepairer` via `IChatClient`. Composes the
LLM's structured-output ability (the JSON Schema map: field name →
selector string) with a careful prompt.

### 4. `ScraperEngineBuilder.WithSelfHealing` — the builder sugar

The convenience that wraps the current extractor in a
`SelfHealingContentExtractor`:

```csharp
public ScraperEngineBuilder WithSelfHealing(ISelectorRepairer repairer)
```

The WebReaper.AI satellite ships
`WithLlmSelfHealing(IChatClient chatClient, LlmExtractorOptions? options = null)`
that builds the `LlmSelectorRepairer` and calls `WithSelfHealing`.

### Bounded scope

- **In-memory cache only** in v1. A persistent (Redis / File) cache is
  a future satellite ADR; the seam already supports it via the
  reference-identity cache.
- **Selector repair only**, not full schema-restructure. The LLM may
  return a new selector for a known field; it cannot add/remove
  fields, change types, or restructure containers. v1 cuts the
  problem to "find selectors that match the same data."
- **No per-host keying.** All pages of a Crawl share one patch
  (assumed: one Schema, one host or homogeneous hosts).
- **No invalidation strategy.** A cached patch lives for the Crawl's
  lifetime. Sites that change again mid-Crawl re-pay the LLM cost on
  the next page.

## Considered options

### (a) Cache per URL — rejected (deferred)

The seam doesn't carry URL. Extending IContentExtractor.ExtractAsync
is a breaking change for a feature that doesn't yet have a multi-
host caller proven to need it.

### (b) Make `ISelectorRepairer` part of `IExtractionRouter` — rejected

Routing and self-heal are different mechanisms. Routing is "swap
extractor on failure." Self-heal is "patch the schema and re-use the
deterministic extractor." Composing them: `SelfHealingContentExtractor`
*is* an `IContentExtractor`, so a router can use it as the primary.

### (c) Persist the patched Schema to ConfigStorage — rejected

ConfigStorage (ADR-0008) round-trips the *original* Schema. Patching
it cross-process would mean the router's primary changes underneath
the crawl. Cleaner shape: patched Schemas live in the self-healing
extractor's cache, separate from the persisted config.

### (d) Validate proposed selectors with a regex first — rejected

A real selector validator means parsing CSS / XPath / JSONPath
properly. AngleSharp's parser is already the fold's; the fold
running with the patched Schema *is* the validation. Cheaper than a
pre-parse.

### (e) Cap repair attempts per Schema — rejected (deferred)

The current "no caching on failed-repair, try again next page" shape
is honest: every page that needs a new fix gets one. A cap belongs
when a budget governor (ADR-0046 deferred) appears.

## Consequences

- **The plan §2.2 ships.** LLM-as-proposer, fold-as-validator, blob-
  store-as-persistence (in-memory in v1) — the cached-selector demotion
  the plan named.
- **The deterministic path stays the hot path.** After the first
  successful repair, every subsequent page of the Crawl runs the
  deterministic fold against the patched Schema — no LLM cost.
- **`IContentExtractor` is unchanged.** The self-healing extractor is
  an adapter; it slots in via `WithSelfHealing`.
- **Composes with the router.** A `SelfHealingContentExtractor` can
  be the primary of an `ExtractionRouter` (rare but possible — "try
  deterministic with self-heal; if even that fails, fall back to LLM
  extraction outright").
- **CONTEXT.md** gains a **Self-healing extractor** term + relationship
  line.

## Implementation

Landed on `ai-native-wave`:

1. **`WebReaper/Core/Parser/Abstract/ISelectorRepairer.cs`** — new
   seam, one method.
2. **`WebReaper/Core/Parser/Concrete/SelfHealingContentExtractor.cs`** —
   the wrapper.
3. **`WebReaper/Builders/ScraperEngineBuilder.cs`** — `WithSelfHealing`.
4. **`WebReaper.AI/LlmSelectorRepairer.cs`** — the satellite repairer.
5. **`WebReaper.AI/LlmExtractorRegistration.WithLlmSelfHealing`** — the
   composing sugar.
6. **`WebReaper.Tests/WebReaper.UnitTests/SelfHealingContentExtractorTests.cs`** —
   pins the cache hit/miss, validate-after-repair, no-cache-on-failed-
   repair, and composition.
7. **CONTEXT.md** — term + relationship line.

### Guardrails

- `dotnet build WebReaper.sln` — 0 errors.
- `dotnet test WebReaper.Tests/WebReaper.UnitTests` — all pass.
- `dotnet test WebReaper.Tests/WebReaper.AI.Tests` — all pass.
- `WebReaper.AotSmokeTest` — unchanged (no AOT-touching code added to
  core).

## References

- ADR-0002 — Schema fold and node-backend seam; the deterministic
  validator the LLM proposer is checked against.
- ADR-0028 — Schema construction guards; patched Schemas must satisfy
  the same construction-time invariants the original does (the
  repairer's emitted Schema goes through Schema.Add → guards fire).
- ADR-0029 — coercion-failure policy; the per-leaf swallow-and-log is
  why a missing field shows as empty-string in the failed result, the
  signal the repairer reads.
- ADR-0039 — `IContentExtractor`; the seam the wrapper implements.
- ADR-0046 — `ExtractionRouter`; the sibling mechanism (different
  shape, different composition; `SelfHealingContentExtractor` can be
  the primary of a router).
- REPOSITIONING-PLAN §2.2 — the cached-selector demotion this ADR
  cashes.
- research/Reviving Web Scraping Library with AI.md §Autonomous
  Self-Healing Pipelines — the "LLM-Proposed Overrides with Local
  Validation" pattern.
