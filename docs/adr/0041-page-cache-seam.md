# `IPageCache` — a cache-read seam on the page loader; `WithMaxAge(TimeSpan)` is the firecrawl-shaped one-liner

## Status

**Accepted — implementation complete** (2026-05-23; landed on branch
`ai-native-wave` off `origin/master`). **Slice 2 of the AI-native
wave** ([REPOSITIONING-PLAN §2.5/§3](../REPOSITIONING-PLAN.md)).
Additive — no Tier-1 break; folds into the unreleased 10.0.0 wave.
Reaches the funnel; ships free, MIT.

## Context

Firecrawl exposes a `maxAge` parameter on `/scrape` (default 172 800 000
ms — 2 days) and `storeInCache`/`minAge` siblings
(docs.firecrawl.dev/features/scrape). Re-running a scrape against the
same URL inside the TTL returns the previously-fetched HTML with no
network call. The research digest (slice-1 message) named this as Free
repeat-call wins, *and* the natural seam an LLM-extractor router
(ADR-0046) and self-heal (ADR-0047) both want — both need to repeatedly
re-walk the same page during development without re-fetching.

WebReaper has no cache today. The Spider unconditionally goes to
`PageLoader.LoadAsync` ([PageLoader.cs:26](../../WebReaper/Core/Loaders/Concrete/PageLoader.cs)),
which dispatches `PageType.Static` to `HttpPageLoadTransport` and
`PageType.Dynamic` to the registered browser transport. Every Job-of-a-
visited-URL is filtered out earlier — `IVisitedLinkTracker`'s atomic
test-and-set (ADR-0022) — so the runtime cost is **only paid on the
first crawl**. The cache is therefore not a per-Job optimisation; it is
a *cross-run* optimisation. Workflows it changes:

- **Iterative crawl development** — running the same Crawl twice (or
  twenty times) while tweaking the Schema or Markdown extractor. Today
  every iteration re-hits the network.
- **LLM-extraction routing (ADR-0046)** — the router will run the
  deterministic fold, validate, and on failure escalate the *same*
  HTML to an LLM. The HTML must be cheap to re-read; it must not be
  re-fetched.
- **Self-healing selectors (ADR-0047)** — proposed selectors are
  validated by re-running the fold against the live page; the page
  must be the same page the failure was observed against.
- **Change-tracking (ADR-0048)** — diffs an extracted record against
  a previously-stored extraction. Doesn't strictly need the HTML cache,
  but composes cleanly: a `MaxAge = TimeSpan.Zero` crawl forces a
  fresh fetch *and* a fresh diff.

The seam shape question: where does the cache sit? Three credible
positions.

1. **Inside each `IPageLoadTransport`** — the HTTP transport caches via
   HttpClient handlers, the browser transport caches separately. Pro:
   no new public surface. Con: two implementations of the cache, both
   bypassed for `--remote` adapters (ADR-0015's
   `WebReaperApiTransport`), no uniform `maxAge` knob.
2. **At `IPageLoader` (the dispatcher)** — one cache wraps both
   transports, keyed by `(url, pageType)` so a Static and a Dynamic
   load of the same URL are distinct entries. Pro: one home, one knob,
   transport-blind. **Chosen.**
3. **Above `IPageLoader` as a decorator** — `CachedPageLoader`
   delegates to an inner `IPageLoader`. Pro: orthogonal composition.
   Con: a second `IPageLoader` adapter, the loader gains "one with
   cache" vs "one without"; the dispatcher pattern (ADR-0004) already
   has its single home (`PageLoader.cs`), and inserting a decorator
   between Spider and the dispatcher gains nothing the collaborator
   pattern doesn't.

Position 2 is the ADR-0004-consistent move: `PageLoader` already owns
the load-mode decision; it gains a *cache-aside* collaborator, the same
way it could gain a retry collaborator (ADR-0026) — a named seam, an
in-memory default, satellites for distributed shapes when they appear.

## Decision

Four moves, all behind one seam, with a one-liner caller surface.

### 1. `IPageCache` — the cache-read/write seam

New public seam in
[WebReaper/Core/Loaders/Abstract/IPageCache.cs](../../WebReaper/Core/Loaders/Abstract/IPageCache.cs):

```csharp
public interface IPageCache
{
    Task<string?> TryReadAsync(string url, PageType pageType, CancellationToken cancellationToken);
    Task WriteAsync(string url, PageType pageType, string document, CancellationToken cancellationToken);
}
```

Two methods, both `CancellationToken`-aware. `TryReadAsync` returns
`null` on miss *or* stale-by-the-implementation's-policy. `WriteAsync`
is fire-and-forget from the caller's perspective — exceptions are
logged and swallowed at the call site (a cache write failure must not
fail a Crawl). Key is `(url, pageType)` because a Static and a Dynamic
load of the same URL can return materially different HTML — the
JS-rendered version is not interchangeable with the server-rendered
shell.

### 2. `NullPageCache` — the default no-cache adapter

[WebReaper/Core/Loaders/Concrete/NullPageCache.cs](../../WebReaper/Core/Loaders/Concrete/NullPageCache.cs).
`TryReadAsync` returns `null`; `WriteAsync` no-ops. The default —
existing behaviour preserved exactly when no cache is configured. The
PageLoader's pre-0041 code path is `cache.TryReadAsync()? ?? await
transport.LoadAsync(); cache.WriteAsync(_)` with the no-op adapter
producing zero allocations beyond the `Task<string?>.FromResult(null)`
(awaiter-optimised).

### 3. `InMemoryPageCache(TimeSpan maxAge)` — the firecrawl-shaped TTL adapter

[WebReaper/Core/Loaders/Concrete/InMemoryPageCache.cs](../../WebReaper/Core/Loaders/Concrete/InMemoryPageCache.cs).
Thread-safe (`ConcurrentDictionary` keyed by `$"{pageType}:{url}"`),
entries hold `(Document, StoredAt)`, `TryReadAsync` returns `null` if
the entry is older than `maxAge`. Eviction is lazy — a stale entry is
overwritten on its next write or remains in memory until the cache is
disposed; a parallelism-degree-bounded crawl will not accumulate
unbounded memory because the visited-link tracker (ADR-0022) bounds
unique URLs. A `Clear()` method is exposed for tests.

`maxAge: TimeSpan.Zero` means "store but never serve" — an explicit
no-op-on-read shape useful for change-tracking (ADR-0048) and for an
ergonomic "force-fresh" mode.

### 4. The builder surface: `WithPageCache` and `WithMaxAge`

Two methods on `ScraperEngineBuilder`:

```csharp
public ScraperEngineBuilder WithPageCache(IPageCache cache)
public ScraperEngineBuilder WithMaxAge(TimeSpan maxAge)
```

`WithPageCache` accepts any `IPageCache` — the seam's open registration
point. `WithMaxAge` is the firecrawl-shaped convenience: it wires
`InMemoryPageCache(maxAge)` for the common case.

### `PageLoader` integration — the cache-aside flow

```csharp
public async Task<string> LoadAsync(PageRequest request, CancellationToken ct)
{
    var cached = await _cache.TryReadAsync(request.Url, request.PageType, ct);
    if (cached is not null) return cached;

    var doc = await Transport(request.PageType).LoadAsync(request, ct);

    try { await _cache.WriteAsync(request.Url, request.PageType, doc, ct); }
    catch (Exception ex) { _logger.LogWarning(ex, "Page cache write failed"); }

    return doc;
}
```

A cache write failure logs and continues — the load itself succeeded;
failing the Crawl on a cache write would be cache-tail-wagging-dog.

### Bounded scope — what this does NOT add

- **No persistent satellite in this slice.** A Redis or File adapter
  is a sibling of `RedisVisitedLinkTracker` / `FileVisitedLinkedTracker`
  shape and lands when a real cross-process-cache caller surfaces. The
  in-memory default is sufficient for the iterative-development and
  router/self-heal use cases that justify the seam today (same
  discipline as ADR-0036's "shape from the second adapter").
- **No `minAge` or `storeInCache` flags** in v1. `maxAge: TimeSpan.Zero`
  covers the "store but never serve" case; `WithPageCache` covers
  custom policies; a `storeOnly: bool` knob is one more parameter
  shaped without a caller and rejected as speculative.
- **No `ICachePolicy` higher-order seam.** A seam-of-a-seam is the
  classic premature-abstraction shape; if heterogeneous policies
  appear, the `IPageCache` implementation absorbs them.
- **No HTTP-cache-header honouring** (`Cache-Control: max-age=...`,
  `ETag`/`If-None-Match`). A real consideration, but firecrawl's
  `maxAge` is *crawler-imposed*, not server-honoured, and the firecrawl
  shape is the wedge; an HTTP-conditional-GET adapter can land later
  behind the same `IPageCache` seam without breaking it.

## Considered options

### (a) Embed caching in each `IPageLoadTransport` — rejected

Two implementations, no uniform knob, opaque to satellites (the
Puppeteer satellite would need its own implementation). The dispatcher
position is where "one place to make load decisions" already lives.

### (b) Decorator pattern (`CachedPageLoader`) — rejected

Two `IPageLoader` adapters where there is now one; the dispatcher
position (ADR-0004) gains an "is it the cached one or the bare one?"
question every caller now answers. The collaborator pattern is the
same composability without the second adapter — `PageLoader` *is* the
home; the cache *is* a collaborator.

### (c) Cache by `PageRequest` (the full record) — rejected

`PageRequest` carries `PageActions` (browser-mode interactions). Two
loads with different actions are not interchangeable; *but* they're
also already keyed apart at `(url, pageType=Dynamic)` because the
actions only fire on Dynamic loads, and a Crawl declaring different
PageActions for the same URL is a configuration mistake or a real
two-shape crawl — either way the cache cannot be wrong. Keying on
`(url, pageType)` is the honest minimum.

### (d) HTTP cache headers / conditional-GET — rejected (deferred)

ETag and `Cache-Control` are how the web tells crawlers about freshness.
Honouring them is a real future feature, but it requires `HttpClient`
handler integration and is asymmetric (no browser-transport equivalent).
Firecrawl's `maxAge` is a *crawler-imposed* TTL; that shape ships now.
An `HttpConditionalPageCache` adapter behind the same `IPageCache` seam
is the future home.

### (e) `WithMaxAge` writes to `ScraperConfig` rather than wires a cache — rejected

`ScraperConfig` is the immutable, serialisable record persisted to
config storage (ADR-0008 round-trip). Adding `MaxAge: TimeSpan?` to it
would round-trip a value that a distributed worker has no way to act
on without an `IPageCache` of its own. Keeping caching as a *runtime
collaborator* (not a config field) is honest — a distributed worker
that wants caching wires its own `IPageCache` (e.g. a Redis adapter),
the in-process driver does the same with the in-memory one.

### (f) Add `bypassCache: bool` on `PageRequest` — rejected (deferred)

Per-request bypass is a real use case (re-extracting a page that
changed mid-crawl), but it's not the v1 wedge. The
`MaxAge = TimeSpan.Zero` shape lets a whole crawl run cache-cold; if
per-page bypass surfaces as a need, a `bypassCache: bool` PageRequest
field lands then with one caller proving the shape.

## Consequences

- **The funnel has its caching story.** A caller writes
  `.WithMaxAge(TimeSpan.FromMinutes(10))` once and re-runs the same
  Crawl repeatedly for ten minutes without network or browser load.
  The CLI (ADR-0043) exposes `--max-age`.
- **The router (ADR-0046) and self-heal (ADR-0047) have free re-reads.**
  Both can re-walk the *same* HTML the deterministic fold first saw,
  with the cache serving the second-and-onward reads.
- **Additive only.** No Tier-1 break: `IPageLoader` is unchanged,
  `PageLoader`'s constructor signature adds one parameter (an
  internal class with one consumer — `SpiderBuilder.Build` — so the
  blast radius is mechanical).
- **`PageLoader`'s responsibility stays singular.** The cache is a
  collaborator, exactly as ADR-0004 frames the transport
  responsibility split.
- **Distributed mode unaffected by default.** A distributed worker
  using the in-memory default behaves like today (no cache shared
  across workers). A worker that wires a custom `IPageCache` (a future
  `WebReaper.Redis` satellite) gets cross-worker sharing for free —
  the seam is positioned correctly for it.
- **AOT-clean.** No reflection, no `dynamic`, no
  `JsonSerializer.Serialize<T>` paths — `ConcurrentDictionary<string,
  CacheEntry>` is the only state. The `WebReaper.AotSmokeTest` gains a
  cache hit/miss case.
- **CONTEXT.md** gains a **Page cache** term, a relationship line
  under the page-loader transports, and the cache-aside flow's one-
  paragraph mention under the Spider's I/O shell description.
- **CLAUDE.md** gains a one-line note — the in-memory cache is not
  shared across processes, configure a satellite for that.

## Implementation

Landed on `ai-native-wave`:

1. **`IPageCache.cs`** — new, public,
   [WebReaper/Core/Loaders/Abstract/](../../WebReaper/Core/Loaders/Abstract/).
2. **`NullPageCache.cs`** — new, internal,
   [WebReaper/Core/Loaders/Concrete/](../../WebReaper/Core/Loaders/Concrete/).
3. **`InMemoryPageCache.cs`** — new, public (so a consumer can `new`
   one with a custom `maxAge` and pass via `WithPageCache`),
   `WebReaper/Core/Loaders/Concrete/`.
4. **`PageLoader.cs`** — `IPageCache` constructor parameter; cache-aside
   `LoadAsync` body.
5. **`SpiderBuilder.cs`** — `PageCache` field with `NullPageCache`
   default; `WithPageCache` registration; cache is passed to
   `PageLoader` at `Build`.
6. **`ScraperEngineBuilder.cs`** — `WithPageCache(IPageCache)` and
   `WithMaxAge(TimeSpan)` (the latter wires `InMemoryPageCache`).
7. **`PageCacheTests`** — new file in
   [WebReaper.Tests/WebReaper.UnitTests/](../../WebReaper.Tests/WebReaper.UnitTests/),
   covering hit/miss, TTL expiry, `(url, pageType)` keying, write-after-
   load semantics, write-failure-doesn't-fail-load, and
   `MaxAge = TimeSpan.Zero` ("store but never serve").
8. **`WebReaper.AotSmokeTest`** — adds an `InMemoryPageCache`
   round-trip case.
9. **`CONTEXT.md` / `CLAUDE.md`** — terms and the relationship line.

### Guardrails (run on the branch tip)

- `dotnet build WebReaper.sln` — 0 errors; pre-existing warning set
  unchanged.
- `dotnet test WebReaper.Tests/WebReaper.UnitTests` — all baseline +
  ADR-0040 tests pass; the new `PageCacheTests` add coverage.
- `WebReaper.AotSmokeTest` — `dotnet publish` Native-AOT zero
  warnings; the published native binary prints `AOT SMOKE: ALL PASS`
  with the new `InMemoryPageCache` case.

## References

- ADR-0004 — the one page-loader transport seam; this ADR keeps the
  dispatcher position singular and adds a collaborator.
- ADR-0022 — the Crawl driver's idempotency authority; the visited-
  link tracker still bounds the cache to first-time-fetched pages, so
  unbounded memory is structurally prevented.
- ADR-0036 — "shape from the second adapter, not for it"; cited for
  the deferral of `minAge` / `storeInCache` / Redis-PageCache /
  HTTP-conditional-GET.
- ADR-0040 — the Markdown extractor; the LLM-ready output is what
  makes "cached HTML re-served fast" useful for the funnel's
  iterative-development story.
- REPOSITIONING-PLAN §3 — "free repeat-call wins" cited as one of
  firecrawl's ideas-to-adopt.
- firecrawl docs (docs.firecrawl.dev/features/scrape) — `maxAge`'s
  shape and 2-day default.
