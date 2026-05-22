# `ISiteMapper` — URL discovery via `sitemap.xml` ∪ root-page links; `WebReaper.SiteMapper.MapAsync()` the one-liner

## Status

**Accepted — implementation complete** (2026-05-23; landed on branch
`ai-native-wave` off `origin/master`). **Slice 3 of the AI-native
wave** ([REPOSITIONING-PLAN](../REPOSITIONING-PLAN.md)). Additive —
no Tier-1 break. Folds into the unreleased 10.0.0 wave. Ships free,
MIT.

## Context

Firecrawl's `/map` (docs.firecrawl.dev/features/map) is a single
endpoint that returns the URLs of a site without running a crawl — a
fast discovery pass over `sitemap.xml` ∪ root-page extracted links. It
costs one credit. Agents use it as the *first* call against a new
domain: "what's here?" The result feeds a subsequent `/scrape` per URL
or — more often — a downstream filter ("only the URLs containing
`/blog/`") before any scraping happens.

WebReaper today has no separable discovery operation. Every URL
walk goes through `ScraperEngineBuilder.Crawl(urls).Extract(schema)` /
`.AsMarkdown()` — the full Spider pipeline, with extraction. To enumerate
a site you must construct a Crawl that throws every page away, which is
the wrong shape: discovery is *one HTTP request* against the sitemap
(plus optionally one against the root page); a Crawl-shaped path
spends the visited-link tracker, the page-processor pipeline, the
sinks, and the engine's parallelism budget on a task that needs none
of them.

The shape this slice asks for is therefore *not* a new Crawl mode but a
**separate, parallel utility** alongside the engine — the same axis
firecrawl draws between `/scrape` (extraction) and `/map` (discovery).

Three credible positions for it.

1. **A new `ICrawlSeed` terminal: `.AsUrls()`.** Symmetric with
   `.Extract` / `.AsMarkdown` (ADR-0040). Pro: one composable lattice.
   Con: it routes a one-HTTP-request operation through the
   Crawl/visited-link/page-processor/sink pipeline; ADR-0001's
   `CrawlOutcome` would need a fourth arm or the seed would need to
   sidestep the Crawl driver. Either way the structural cost is too
   high for the gain.
2. **A static helper in some `WebReaper.*` namespace.** A free
   function, no seam. Pro: no abstraction tax. Con: not unit-testable
   without a real HTTP server, and a Redis/file/mock variant of the
   underlying HTTP fetch can never be substituted.
3. **A new seam `ISiteMapper` with a default `SiteMapper` adapter,
   plus a static `ScraperEngineBuilder.MapAsync` convenience.**
   Pro: testable; CLI / MCP can substitute a stub; an authenticated
   mapper (cookies, proxies) is a future drop-in. Con: a seam with one
   adapter in v1. **Chosen** — because the second adapter is genuinely
   imminent (the CLI's `webreaper map` and the agent skill both want a
   substitutable mapper for offline tests; an authenticated mapper for
   logged-in sitemaps is the third), and the deletion test on the
   default `SiteMapper` would relocate ~80 lines of HTTP+XML parsing
   into the static helper — concentrated complexity, not pass-through
   ceremony.

The decisions inside the mapper:

- **What's a "site map"?** The union of (a) `sitemap.xml` at the site
  root, parsed (and recursed if it's a sitemap index), plus (b) all
  `<a href>` URLs on the root page resolved to absolute URLs, filtered
  to the same host. firecrawl's `/map` does the same union plus an
  optional keyword filter; we ship the union now, the keyword filter
  next (`MapOptions.Search`).
- **`robots.txt`?** Parse it for `Sitemap:` lines and follow them. Many
  real sites name their sitemap elsewhere (`/sitemap_index.xml`,
  CDN-hosted). The robots.txt parse is also a free door to ADR-0048's
  / a future ADR-0049's compliance work — same loader, two readers.
- **Recursive sitemap-index following.** Real sitemaps are routinely
  sitemap-indexes pointing at per-section sitemaps. v1 follows one
  level of nesting (the common shape); deeper nesting is deferred —
  the seam doesn't change, only the default implementation.
- **Host filter.** A root page's `<a href>` set typically includes
  off-site links (social, ads, CDNs). The default keeps only same-host
  URLs; an opt-in `MapOptions.AllowOffsite = true` widens.
- **No JS rendering.** A site that exposes its links only via JS
  isn't sitemap-discoverable by any reasonable mapper. If a caller
  needs that, they construct a `CrawlWithBrowser` crawl.
- **No proxies in v1.** The default mapper uses `HttpClient` with a
  single fixed User-Agent. A caller needing proxy support implements
  `ISiteMapper` themselves; the seam's open registration accommodates
  it. (firecrawl's mapper runs on Fire-engine; ours acknowledges
  scope.)

## Decision

Five moves, one seam, one one-liner.

### 1. `ISiteMapper` — the discovery seam

New public seam in
[WebReaper/Core/Mapping/ISiteMapper.cs](../../WebReaper/Core/Mapping/ISiteMapper.cs):

```csharp
public interface ISiteMapper
{
    Task<IReadOnlyList<string>> MapAsync(
        string url,
        MapOptions? options = null,
        CancellationToken cancellationToken = default);
}
```

A single method returning a deduplicated, ordered list of URLs. The
order is not lexicographic — it reflects the union: sitemap URLs
first (in file order), then root-page link URLs (in DOM order), with
duplicates collapsed in-favour-of-first-seen. This gives downstream
filters a deterministic input — the same site mapped twice returns
the same list — without imposing a sort the agent has to undo.

### 2. `MapOptions` — the discovery knobs

```csharp
public sealed record MapOptions(
    int MaxUrls = 1000,
    bool IncludeSitemap = true,
    bool IncludeRootPageLinks = true,
    bool AllowOffsite = false,
    string? Search = null);
```

`MaxUrls` is a hard cap (the mapper returns at most this many).
`IncludeSitemap` / `IncludeRootPageLinks` toggle the two sources
independently — `Search` (filter to URLs containing this substring,
case-insensitive) is the firecrawl-shaped ranking proxy.

### 3. `SiteMapper` — the default adapter

[WebReaper/Core/Mapping/SiteMapper.cs](../../WebReaper/Core/Mapping/SiteMapper.cs).
Default constructor uses a per-call `HttpClient`. A second constructor
accepts an `HttpMessageHandler` factory — that's the test seam and
the integration point for cookies/proxies/decorating-handlers (the
proxy-rotating mapper is the future second adapter; today's caller
needing it injects via the handler factory rather than asking for a
whole new `SiteMapper` overload set).

Pipeline:

1. `GET /robots.txt`. Parse out every `Sitemap:` line.
2. If no `Sitemap:` lines, fall back to `GET /sitemap.xml`.
3. For each discovered sitemap, `GET` and parse:
   - `<urlset>` — direct URLs.
   - `<sitemapindex>` — recurse one level into each child sitemap.
4. `GET` the root URL. Extract every `<a href>`, resolve to absolute.
5. Union sitemap URLs (in order) and root-page links (in order),
   dedupe, filter by host (unless `AllowOffsite`), filter by `Search`
   substring (case-insensitive) if provided, cap at `MaxUrls`.

Errors at each step (a 404 on `/sitemap.xml`, malformed XML, the root
page returning 5xx) are logged at `Information` and the step is
skipped — discovery is best-effort; a partial result is more useful
than a thrown exception. A genuinely non-existent host
(`HttpRequestException`) propagates.

### 4. `WebReaper.Builders.ScraperEngineBuilder.MapAsync` — the one-liner

```csharp
public static Task<IReadOnlyList<string>> MapAsync(
    string url,
    MapOptions? options = null,
    CancellationToken cancellationToken = default)
    => new SiteMapper().MapAsync(url, options, cancellationToken);
```

The funnel ergonomic. The CLI (ADR-0043) is `webreaper map <url>`; the
agent skill calls the same.

### 5. Bounded scope — what this does NOT add

- **No deep sitemap-index recursion (>1 level).** Edge case; the seam's
  open registration accommodates a custom adapter when the case appears.
- **No keyword ranking / similarity scoring.** `MapOptions.Search` is a
  substring filter, not the embedding-similarity ranking firecrawl's
  `/map` does. Real ranking is an LLM-or-embeddings call; this slice is
  deterministic. The router (ADR-0046) and a future ADR can layer
  ranking later.
- **No depth-N link crawling.** "Find URLs by walking N hops" is what
  `Crawl(urls).Extract(schema)` does today; this slice doesn't
  duplicate it.
- **No JS-rendered mapping.** Static HTTP only; documented limitation.
- **No proxy/cookie wiring.** Custom `HttpMessageHandler` via the
  test-seam constructor is the bridge for now.

## Considered options

### (a) A `.AsUrls()` `ICrawlSeed` terminal — rejected

Discovery routed through the Crawl pipeline pays a structural cost
(visited-link tracker, page processors, sinks, parallelism) for a
task that needs none of it. The lattice symmetry is shallow — the
operation isn't a Crawl. Two separate utilities with separate
ergonomics is the honest shape.

### (b) A static helper, no seam — rejected

Untestable without a real HTTP server; no substitution point for the
CLI / MCP / authenticated-mapper variants. The seam pays for itself
the first time a unit test wants a stub or a satellite wants a
proxy-aware adapter.

### (c) Robots.txt as a separate `IRobotsParser` seam — rejected (deferred)

A real future feature (the compliance work in the plan's
[ADR-0019](../REPOSITIONING-PLAN.md) lane), but unrelated to discovery
specifically. The two-line `Sitemap:` parse the mapper does is fine
inlined; if/when allow/deny rules and crawl-delay handling land,
that's the right time to extract `IRobotsParser`.

### (d) Synchronous return — rejected

Discovery makes 1–N HTTP calls. `Task<IReadOnlyList<string>>` is the
honest shape; sync would force `.GetAwaiter().Result()` at every
caller.

### (e) Streaming `IAsyncEnumerable<string>` — rejected (deferred)

For sites with very large sitemaps (>10k URLs), streaming is the
right shape. v1's `MaxUrls = 1000` default makes the materialised list
acceptable; if a caller surfaces with a real >10k case, the seam can
gain a `MapStreamAsync` overload.

### (f) Bake `ISiteMapper` into the engine — rejected

The engine is for *extraction*; the mapper is for *discovery*. Folding
them gives the engine a responsibility it doesn't have and breaks the
ADR-0022 "the engine drives the Crawl, nothing else" framing.

## Consequences

- **The funnel has its discovery story.** A caller writes
  ```csharp
  var urls = await ScraperEngineBuilder.MapAsync(
      "https://example.com",
      new MapOptions { Search = "/blog/", MaxUrls = 50 });
  ```
  to enumerate fifty blog URLs without a Crawl. The CLI exposes
  `webreaper map`.
- **The CLI has a first-class command.** ADR-0043 wires `webreaper map`
  to this seam directly — no per-URL scrape, just the URL list.
- **The MCP server (ADR-0049) has a tool.** `map(url, search?)` is one
  of the four headline tools the agent skill exposes.
- **Open registration for an authenticated mapper.** A future
  `WebReaper.Auth` satellite implementing `ISiteMapper` with cookie /
  proxy / OAuth support is a clean drop-in.
- **AOT-clean.** `XDocument` is the only XML parser used (no
  reflection-driven serialisation); `HttpClient` is AOT-supported. The
  `WebReaper.AotSmokeTest` gains a SiteMapper structure-only case
  (we cannot make real HTTP from the smoke test, but the
  AOT-compileability of `SiteMapper` is the property we want pinned).
- **CONTEXT.md** gains a **Site mapper** term and a relationship line.

## Implementation

Landed on `ai-native-wave`:

1. **`ISiteMapper.cs`** — new, public, Tier-1; one method
   `MapAsync(url, options?, ct)`.
2. **`MapOptions.cs`** — new, public, Tier-1; record with five fields.
3. **`SiteMapper.cs`** — new, public, Tier-1; default implementation
   using `HttpClient` + `XDocument` + AngleSharp. The constructor takes
   an optional `Func<HttpMessageHandler>` factory for test/proxy
   substitution.
4. **`ScraperEngineBuilder.MapAsync`** — new static helper, sugar over
   `new SiteMapper().MapAsync`.
5. **`SiteMapperTests`** — new file; covers sitemap parsing,
   sitemap-index recursion, robots.txt `Sitemap:` extraction, root-
   page link extraction, host filter, `Search` substring filter,
   `MaxUrls` cap, ordering (sitemap then root-page), failure
   resilience (404 → skip, malformed XML → skip).
6. **`WebReaper.AotSmokeTest`** — adds a `SiteMapper` structure case.
7. **`CONTEXT.md`** — term + relationship line.

### Guardrails (run on the branch tip)

- `dotnet build WebReaper.sln` — 0 errors; warning set unchanged.
- `dotnet test WebReaper.Tests/WebReaper.UnitTests` — every test
  passes; `SiteMapperTests` add coverage using a stub
  `HttpMessageHandler` to avoid network.
- `WebReaper.AotSmokeTest` — Native-AOT publish 0 IL warnings;
  published binary prints `AOT SMOKE: ALL PASS` including the new
  case.

## References

- ADR-0036 — link extraction as a concrete function, not a seam; the
  reasoning for inlining tiny AngleSharp queries (the mapper does its
  own root-page link extraction rather than importing the internal
  `LinkExtractor`).
- ADR-0040 — the no-schema Markdown wedge; mapping pairs with it
  (discover URLs, then `.AsMarkdown()` each one for an LLM).
- ADR-0041 — the page cache; the mapper's HTTP gets bypass it
  intentionally (mapping is one-shot; caching the sitemap.xml across
  Crawls is a future feature behind the same `IPageCache` seam).
- REPOSITIONING-PLAN — firecrawl-derived ideas-to-adopt list cites
  `/map` as a cheap-to-ship, immediately-useful funnel asset.
- firecrawl docs (docs.firecrawl.dev/features/map) — the shape and
  pricing this ADR borrows.
