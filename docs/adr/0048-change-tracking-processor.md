# `ChangeTrackingProcessor` — snapshot Markdown per URL, emit `change_status` on the page-processor pipeline

## Status

**Accepted — implementation complete** (2026-05-23; landed on branch
`ai-native-wave` off `origin/master`). **Slice 9 of the AI-native wave**.
Firecrawl-research-digest idea #4 ("Change-tracking as an IPageProcessor
— snapshot markdown per URL, expose ChangeStatus, lands cleanly on
ADR-0038"). Additive — uses the existing `IPageProcessor` seam (ADR-0038).
Folds into the unreleased 10.0.0 wave; ships free, MIT.

## Context

The firecrawl `/scrape` endpoint surfaces *change-tracking* as a format
on the composable `formats: []` array — re-running the same scrape
returns a `changeTracking` field reporting **new / same / changed /
removed** plus an optional diff (docs.firecrawl.dev/features/change-
tracking). For monitoring workflows ("did the product page change since
yesterday?") it eliminates the need for a custom diff pipeline.

WebReaper has the perfect home for this: ADR-0038's `IPageProcessor`
pipeline — the post-extraction, pre-sink stage where a record is
inspected, enriched, or dropped. A change-tracking processor:

1. Computes a stable hash of the page's Markdown extraction (ADR-0040
   — robust to whitespace / template noise).
2. Looks up the prior hash for the page's URL in a store.
3. Decides `new` (no prior) / `same` (hash matches) / `changed` (hash
   differs).
4. Writes the current hash back for the next pass.
5. Annotates the `ParsedData` with `change_status` (and optionally
   `previous_hash` for downstream diff tooling).

### What about "removed"?

A page processor only sees pages that *were visited*. "Removed" can
only be detected by something that knows the prior full URL set and
sees the current crawl miss them — a Crawl-driver-level pass, not a
per-page processor. v1 ships **new / same / changed** only; a
post-Crawl "removed-page sweep" is a future feature requiring the
visited-link tracker's state across crawls.

### Cache shape

A separate seam from `IPageCache` (ADR-0041). The page cache stores
*full HTML* for re-reads; the change-tracking store needs only the
*hash* per URL. Different lifecycles (the page cache has a TTL; the
change store does not), different keys (page cache keys on URL+
page-type, change store on URL alone — a Static vs Dynamic load of
the same URL is the same page conceptually). One seam, two adapters
would conflate.

New seam: `IChangeStore`. In-memory default ships in core; persistent
adapters (Redis / File) land later.

### Bounded scope

- **New / same / changed only.** No `removed` detection in v1 (needs
  cross-Crawl state outside the processor).
- **Hash, not diff.** v1 reports the *status*, not the diff text.
  Diff generation is a future enhancement — straightforward but the
  agent / monitoring callers that ship today work with status alone.
- **In-memory store default.** Persistent stores arrive when a real
  caller surfaces (same discipline as ADR-0041's page cache).
- **Markdown-based hash** (not raw HTML hash). The Markdown
  extractor strips template noise (nav / footer / ads) — the hash is
  robust to dynamic content that doesn't affect the reading content.

## Decision

Three pieces.

### 1. `IChangeStore` — the seam

[WebReaper/Processing/Abstract/IChangeStore.cs](../../WebReaper/Processing/Abstract/IChangeStore.cs).

```csharp
public interface IChangeStore
{
    Task<string?> TryReadAsync(string url, CancellationToken cancellationToken);
    Task WriteAsync(string url, string hash, CancellationToken cancellationToken);
}
```

Returns `null` for an unseen URL. The hash is opaque to the store —
each adapter decides whether to ship a key/value file, a Redis SET,
or a SQL row.

### 2. `InMemoryChangeStore` — the default

[WebReaper/Processing/Concrete/InMemoryChangeStore.cs](../../WebReaper/Processing/Concrete/InMemoryChangeStore.cs).
`ConcurrentDictionary<string, string>`. Per-process; not shared across
Crawls unless the consumer wires a satellite.

### 3. `ChangeTrackingProcessor` — the processor

[WebReaper/Processing/Concrete/ChangeTrackingProcessor.cs](../../WebReaper/Processing/Concrete/ChangeTrackingProcessor.cs).
An `IPageProcessor` taking an `IChangeStore`. Each page:

1. Run the Markdown extractor on `context.Html` (same code path as
   ADR-0040 — no LLM dependency).
2. SHA-256 hash the resulting Markdown.
3. Look up the prior hash for `context.Data.Url`.
4. Determine status (`new` / `same` / `changed`).
5. Write the new hash to the store.
6. Mutate `context.Data.Data` to add `change_status` and
   `previous_hash` keys; return `PageVerdict.Keep`.

Sinks see the enriched `JsonObject` as usual.

### 4. `ScraperEngineBuilder.WithChangeTracking` — the builder sugar

```csharp
public ScraperEngineBuilder WithChangeTracking(IChangeStore? store = null)
```

`null` defaults to `InMemoryChangeStore`. The processor is added to
the pipeline via `SpiderBuilder.AddProcessor`.

## Considered options

### (a) Bake change-tracking into `IPageCache` — rejected

Different lifecycles, different keys, different semantics. The page
cache caches the HTML body; the change store stores a hash to detect
mutations. Conflation would force one TTL for both.

### (b) Use the visited-link tracker — rejected

The tracker stores "have I visited?" — boolean, ADR-0022 atomic-add
contract. Extending it to carry a hash breaks the contract and the
atomic-test-and-set the Crawl driver relies on.

### (c) Diff in v1 — rejected (deferred)

Real diffing means choosing a unified-diff library or generating one.
Out of scope; the agent / monitoring caller can compute the diff
from the stored Markdown when status is `changed` (the snapshot is
available — the processor stores the *hash*, but a v2 store could
also stash the Markdown).

### (d) Hash raw HTML — rejected

Modern pages embed timestamps, ad-rotation, session IDs everywhere
in raw HTML — hashing it would mark every page `changed` every run.
Markdown's chrome-stripped, content-focused output is the stable
hashable surface.

## Consequences

- **The funnel has a first-class monitoring story.** A caller adds
  `.WithChangeTracking()` and gets `change_status` on every record,
  no custom diff code.
- **Lands on the existing ADR-0038 seam.** No new pipeline mechanism;
  it's just a processor.
- **The Markdown extractor (ADR-0040) gets a second consumer.** Same
  code path, now serving the change-tracking hash AND the
  `.AsMarkdown()` terminal.
- **CONTEXT.md** gains a **Change tracking** term + relationship line.

## Implementation

Landed on `ai-native-wave`:

1. **`WebReaper/Processing/Abstract/IChangeStore.cs`** — new seam.
2. **`WebReaper/Processing/Concrete/InMemoryChangeStore.cs`** — default.
3. **`WebReaper/Processing/Concrete/ChangeTrackingProcessor.cs`** —
   the processor.
4. **`WebReaper/Builders/ScraperEngineBuilder.cs`** —
   `WithChangeTracking`.
5. **`WebReaper.Tests/WebReaper.UnitTests/ChangeTrackingProcessorTests.cs`** —
   pins new / same / changed across two visits to the same URL.

### Guardrails

- `dotnet build WebReaper.sln` — 0 errors.
- `dotnet test WebReaper.Tests/WebReaper.UnitTests` — all pass.
- `WebReaper.AotSmokeTest` — unchanged (no reflection added).

## References

- ADR-0031 — `ParsedData` URL-merge; the processor's `change_status`
  key sits next to the merged URL.
- ADR-0038 — the page-processor seam this processor implements.
- ADR-0040 — Markdown extractor; the hash subject.
- ADR-0041 — page cache; sibling but separate seam.
- firecrawl docs (docs.firecrawl.dev/features/change-tracking) — the
  shape borrowed.
- Research digest #4 — "Change-tracking as an IPageProcessor."
