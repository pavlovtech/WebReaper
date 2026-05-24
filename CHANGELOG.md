# Changelog

## 10.0.0 — AI-native funnel + semantic actions, on a deepened architecture; MIT relicense (breaking)

The headline release of the year — 25 ADRs (0025–0050, with ADR-0017 the
parallel licence move) — splits into three arcs. The first is the *staged
builder* that closes the last runtime construction trap (ADR-0025). The
second is *architecture deepening* — two review waves (ADR-0026..0031 and
ADR-0032..0039) that turn the in-process Crawl driver into a small,
audit-clean composition over named seams. The third is the *AI-native wave*
(ADR-0040..0049): a no-schema Markdown terminal, a CLI + Agent Skill, an LLM
extractor satellite, a `[ScrapeSchema]` source generator, a deterministic→LLM
extraction router, a self-healing extractor, a change-tracking processor, and
an MCP server satellite — all bolted onto the seams the architecture
deepening exposed. **In parallel, the project is relicensed GPL-3.0-or-later
→ MIT** (ADR-0017) to remove the funnel's last adoption-friction edge for
downstream consumers and SaaS integrators. *Every* breaking change is in this
release; the post-10.0.0 cadence returns to additive minor releases on the
seams introduced here.

### A scrape begins with a Crawl seed (ADR-0025)

The builder front door no longer has a runtime trap. Start URLs and a schema
were a *runtime* `InvalidOperationException` from `ConfigBuilder.Build()` if you
forgot them, documented only by a `CLAUDE.md` gotcha — the exact
runtime/implicit failure the project's signature move (ADR-0001, ADR-0022)
exists to make structurally impossible. ADR-0025 makes it so: a scrape now
begins with a **Crawl seed**. `ScraperEngineBuilder.Crawl(urls)` /
`.CrawlWithBrowser(urls)` are *static* and return `ICrawlSeed`; its only member,
`.Extract(schema)`, returns the configurable `ScraperEngineBuilder`. That
builder's constructor is `internal`, so the build terminals are unreachable
without a seed and a schema — "build with no start URLs or no schema" has no
representation to construct, not a throw to hit. Rationale, the grilled
alternatives (type-state phantom generics; the multi-param factory; R-narrow),
the trilemma surfaced at implementation and the two-seam resolution:
[`docs/adr/0025-staged-builder-entry.md`](docs/adr/0025-staged-builder-entry.md).

#### Breaking changes

- **The entry point moved.** `new ScraperEngineBuilder()` (public ctor) +
  `.Get(...)` / `.GetWithBrowser(...)` + `.Parse(...)` are replaced by the
  static `ScraperEngineBuilder.Crawl(...)` / `.CrawlWithBrowser(...)` →
  `ICrawlSeed.Extract(schema)` → the builder. `ScraperEngineBuilder`'s
  constructor is `internal` (test-only via `InternalsVisibleTo`); `Get`,
  `GetWithBrowser`, `Parse` are gone.
- **`ConfigBuilder` is `internal`.** Its sole external use — the distributed
  start endpoint's `new ConfigBuilder()….Build()` — is absorbed by the seed's
  gated `ScraperEngineBuilder.Build()` terminal
  (`Crawl(...).Extract(...).Build()` → `ScraperConfig`). The two
  `ConfigBuilder.Build()` throws and the `CLAUDE.md` builder-order gotcha are
  deleted by construction.
- **The distributed-worker reduced shell is its own type.**
  `new ScraperEngineBuilder()….BuildSpider()` becomes
  `new DistributedSpiderBuilder()….BuildSpider()` (ADR-0009). It has a public
  constructor and no `BuildAsync` — so the structural guarantee is absolute and
  the worker stays seedless ("two seams, not one bug"). `BuildSpider()` is
  removed from `ScraperEngineBuilder`; the worker no longer wires the
  driver-owned visited-link tracker (ADR-0022).
- **Zero satellite ripple.** Every `this ScraperEngineBuilder` satellite
  extension is unchanged and works after `.Extract(...)`. Distributed workers
  wire shared adapters by direct construction (public satellite concretes), as
  the canonical `AzureFuncs` example already did.

#### Migration

`new ScraperEngineBuilder().Get(url)….Parse(schema)….BuildAsync()` →
`ScraperEngineBuilder.Crawl(url).Extract(schema)….BuildAsync()` — move the
schema up to `.Extract`, right after the seed; everything else chains unchanged,
in order, after it. `.GetWithBrowser` → `.CrawlWithBrowser`. The distributed
start endpoint's `new ConfigBuilder()….Build()` →
`ScraperEngineBuilder.Crawl(...).Extract(...)….Build()`. The distributed
worker's `new ScraperEngineBuilder()….BuildSpider()` →
`new DistributedSpiderBuilder()….BuildSpider()` (drop the now-unneeded
`.WithLinkTracker(...)` — the driver owns it, ADR-0022). No behavioural change;
the guardrail (whole-solution build, 94 unit + 27 satellite tests, Native-AOT
smoke) is green.

### Architecture deepening — wave 1 (ADR-0026 – ADR-0031)

A wave of internal-architecture deepening landed on `master` after ADR-0025,
from a fresh architecture review. Each change is internal-only or
behaviourally-additive except for the narrow breaking edges called out below;
full design, considered options and rejected alternatives are in each linked
ADR.

- **Retry around the per-Job Spider call is a named seam; Polly leaves core
  (ADR-0026).** The `internal static Infra.Executor` Polly pass-through becomes
  `IRetryPolicy.ExecuteAsync<T>`, a documented seam; the core default
  `FixedAttemptsRetryPolicy` is hand-rolled (four attempts, no delay), so the
  `Polly` package leaves the core dependency graph. A custom policy — e.g. a
  Polly resilience pipeline with exponential backoff — wires in through
  `ScraperEngineBuilder.WithRetryPolicy(...)`. A latent bug is fixed:
  `OperationCanceledException` is no longer retried, so cancellation propagates
  promptly. Additive, internal-only.
  [`docs/adr/0026-retry-policy-seam.md`](docs/adr/0026-retry-policy-seam.md)

- **Shared raw-extraction helper for the AngleSharp backends (ADR-0027).** The
  attribute / inner-HTML / text dispatch that was copy-pasted across the CSS
  and XPath `ISchemaBackend` implementations moves to one internal
  `AngleSharpRawExtractor`; each backend's `ExtractRaw` shrinks to "apply this
  backend's quirks, then delegate." The `ISchemaBackend<TNode>` seam and the
  ADR-0007 CSS `src`→`title` behavioural difference are unchanged.
  Internal-only.
  [`docs/adr/0027-anglesharp-raw-extractor.md`](docs/adr/0027-anglesharp-raw-extractor.md)

- **Schema construction enforces its grammar at the Add site (ADR-0028).**
  `Schema.Add` rejects an empty `Field`, an empty leaf `Selector`, or an empty
  list-container `Selector` at the add call instead of failing later in the
  fold; `Schema.ListOf(field, selector, …children)` is a new named factory for
  the list-of-objects shape. *Narrowly breaking:* a Schema constructed with one
  of those defects now throws `ArgumentException` at construction rather than
  silently dropping a field or aborting the parse.
  [`docs/adr/0028-schema-construction-guards.md`](docs/adr/0028-schema-construction-guards.md)

- **The Schema fold's coercion-failure policy is pinned, with differentiated
  logs (ADR-0029).** The per-leaf swallow-and-log is now the documented
  contract: a coercion failure (`FormatException` / `OverflowException`) is
  logged with a coercion-specific message naming the target type and field,
  distinct from the catch-all "unexpected error extracting field" message — so
  operators can tell *page had bad data* from *selector is wrong*. Behaviour at
  the contract surface is unchanged (the field is left unset; a noisy page
  never aborts the crawl). Internal-only.
  [`docs/adr/0029-coercion-failure-policy.md`](docs/adr/0029-coercion-failure-policy.md)

- **LinkPathSelector enforces its grammar at construction (ADR-0030).** The
  `LinkPathSelector` primary constructor rejects an empty `Selector`, an empty
  (non-null) `PaginationSelector`, and `PageActions` paired with
  `PageType.Static`; `LinkPathSelector.Follow` / `.Paginate` are new named
  factories for the two intent-shapes. The four `ConfigBuilder` selector-chain
  methods are unchanged in signature. *Narrowly breaking:* a `LinkPathSelector`
  constructed with one of those defects now throws at construction instead of
  failing late at the crawl.
  [`docs/adr/0030-link-path-selector-construction-guards.md`](docs/adr/0030-link-path-selector-construction-guards.md)

- **ParsedData's construction owns the URL-merge (ADR-0031).** The page URL is
  folded into `ParsedData.Data` under `"url"` at construction, so every sink
  writes `Data` as-is and none re-merges the URL — console output now includes
  the URL, which it previously omitted. The Crawl driver hands each sink its
  own deep-clone of `Data`, so concurrent sinks never share a `JsonObject`.
  `ParsedData`'s public shape is unchanged; *one narrow edge:* constructing a
  `ParsedData` mutates the passed `JsonObject` to fold the URL in.
  [`docs/adr/0031-parseddata-url-merge.md`](docs/adr/0031-parseddata-url-merge.md)

### Architecture deepening — wave 2 (ADR-0032 – ADR-0039)

A second review wave following wave-1. The Crawl driver becomes a small
composition over named seams; several latent footguns in durable adapters
fall out by construction; and the extraction surface is renamed honestly,
opening the seat the AI-native wave drops into.

- **The Crawl driver's stop rule becomes a module; the latch's credit protocol collapses to one atomic op (ADR-0032).** *"Is the Crawl over, and why?"* lands in a new internal `StopRule` that composes the **Outstanding-work latch** and the page limit behind one verdict; `IOutstandingWorkLatch` loses `AddAsync` and `SignalProcessedAsync` takes a child count, so the two-call credit-ordering footgun is gone — the Redis latch does one round-trip, not two. *Breaking:* a Tier-1 public seam re-signatures; `RedisOutstandingWorkLatch` is updated, custom latch adapters must adopt the new shape.
  [`docs/adr/0032-stop-rule-module.md`](docs/adr/0032-stop-rule-module.md)

- **Async adapter warm-up becomes an opt-in capability the Crawl driver drives (ADR-0033).** A new public `IAsyncInitializable` (one idempotent `InitializeAsync()`) replaces the ad-hoc `Task Initialization` property; the ten durable adapters get pure constructors backed by `Lazy<Task>`, and the driver warms scheduler + tracker + every sink uniformly before the loop — sinks are warmed where they used to self-guard on first emit. *Breaking:* `IScheduler` and `IVisitedLinkTracker` lose `Initialization`; durable adapters must implement `IAsyncInitializable`. `IScraperSink` is unchanged.
  [`docs/adr/0033-async-warmup-seam.md`](docs/adr/0033-async-warmup-seam.md)

- **The Spider takes its run-scoped inputs at construction; `IScraperConfigStorage` leaves the shell (ADR-0034).** `Spider`'s constructor becomes `(ICrawlStep, IPageLoader, bool headless, Schema? parsingScheme)` and the per-Job `GetConfigAsync()` round-trip is gone — config storage is purely the Crawl driver's concern now. *Breaking:* `DistributedSpiderBuilder` loses `WithConfigStorage` / `WithFileConfigStorage`; `BuildSpider()` gains a required `ScraperConfig` parameter, making "build a worker with no config" a compile error. `ScraperEngineBuilder.WithConfigStorage` is unchanged.
  [`docs/adr/0034-spider-config-at-construction.md`](docs/adr/0034-spider-config-at-construction.md)

- **`PageAction` becomes a closed sum of typed arms (ADR-0035).** `PageAction` is now an abstract record with six sealed-record arms (`Click`, `Wait`, `ScrollToEnd`, `EvaluateExpression`, `WaitForSelector`, `WaitForNetworkIdle`), each carrying typed fields — the `PageActionType` enum, the `object[] Parameters`, the ~75-line kind-tagging codec and the transport's runtime casts are all gone. `EvaluateExpression` and `WaitForSelector` (publicly advertised but silently unwired) now actually run. *Breaking:* `PageAction` re-shaped, `PageActionType` removed, wire format changes; `PageActionBuilder`'s public signatures are unchanged.
  [`docs/adr/0035-pageaction-closed-sum.md`](docs/adr/0035-pageaction-closed-sum.md)

- **Link extraction collapses to a concrete function; `ILinkParser` is removed (ADR-0036).** The one-adapter, one-caller `ILinkParser` seam becomes a single `internal static LinkExtractor.GetLinksAsync` called directly by `CrawlStep`. A latent mid-crawl crash on `<a>` elements without `href` is fixed in the rewrite — hrefless anchors are skipped, not `ArgumentNullException`. *Breaking:* `ILinkParser` and `LinkParserByCssSelector` removed; no fluent-path migration exists (there was never a `WithLinkParser`).
  [`docs/adr/0036-link-extraction-not-a-seam.md`](docs/adr/0036-link-extraction-not-a-seam.md)

- **Stop ceases the Crawl driver's consumption; `IScheduler.Complete()` is removed (ADR-0037).** Termination is now a consumer-side cancel of `GetAllAsync`'s token — uniform across every scheduler. Previously `Complete()` was a no-op on `FileScheduler` / `RedisScheduler` / `SqliteScheduler` / `AzureServiceBusScheduler`, so a durable scheduler with `StopWhenAllLinksProcessed()` ran forever; that footgun is gone. `GetAllAsync`'s token contract is now stated explicitly. *Breaking:* `IScheduler.Complete()` removed; in-flight Jobs abort on a cutoff instead of draining.
  [`docs/adr/0037-stop-ceases-consumption.md`](docs/adr/0037-stop-ceases-consumption.md)

- **The post-extraction surface becomes two seams: a page-processor pipeline and the Sink (ADR-0038).** A new `IPageProcessor` / `PageContext` / `PageVerdict` (`Kept | Dropped`) surface in `WebReaper.Processing` runs ordered over each extracted page before the sink fan-out — enrich, observe, filter, or replace the record, with the raw HTML and `Schema` in hand. `Subscribe` keeps its signature but is now sugar over an internal `DelegateSink` (closing the ADR-0031 shared-record leak in passing). *Breaking:* `ScraperEngineBuilder.PostProcess` and the public `Metadata` type are removed; move a `PostProcess` callback to `.Process(...)`.
  [`docs/adr/0038-page-processor-seam.md`](docs/adr/0038-page-processor-seam.md)

- **`IJsonContentParser` becomes `IContentExtractor`; the three `*ContentParser` shells collapse onto `SchemaFold` (ADR-0039).** The seam is renamed honestly (the "Json" qualifier was a 6.0.0 fossil), `SchemaContentParser<TNode>` becomes the public `SchemaFold<TNode>`, and the pass-through `AngleSharpContentParser` / `XPathContentParser` / `JsonContentParser` shells are deleted — `SpiderBuilder` constructs the fold directly, the same way ADR-0002's custom-backend extension path does. *Breaking:* `IJsonContentParser` → `IContentExtractor`, `ParseToJsonAsync` → `ExtractAsync`, `WithContentParser` → `WithContentExtractor`; `WithJsonContentParser` / `WithXPathContentParser` keep their names.
  [`docs/adr/0039-content-extractor-seam.md`](docs/adr/0039-content-extractor-seam.md)

### AI-native wave (ADR-0040 – ADR-0049)

The strategic move of the release. The architecture deepening exposed clean
seams for *content extraction* (ADR-0039), *post-extraction processing*
(ADR-0038), and *page loading* (ADR-0004). This wave drops AI-native
features onto those seams: a no-schema Markdown terminal, a CLI, an LLM
extractor satellite, a Roslyn `[ScrapeSchema]` source generator
(Pydantic-parity Python cannot match), a deterministic→LLM router, a
self-healing extractor, change-tracking, and MCP interop. Core stays
dependency-light and AOT-clean; every heavy dependency stays quarantined in
its satellite per ADR-0009.

- **`.AsMarkdown()` — a second `ICrawlSeed` terminal returning LLM-ready Markdown (ADR-0040).** `ICrawlSeed` gains `AsMarkdown()` alongside `Extract(Schema)`; a new `MarkdownContentExtractor` (AngleSharp-driven, AOT-clean, zero new transitive deps) cleans the DOM via a tag-based Readability heuristic and emits `{ title, markdown }`. The schema-required gate becomes a strategy-choice lattice — ADR-0025's structural promise is stated correctly: *a Crawl declares its extraction strategy before `BuildAsync`*. *Breaking:* `ICrawlSeed` gains one method; `IContentExtractor`'s doc widens (the schema requirement is strategy-local).
  [`docs/adr/0040-markdown-extraction-seed-terminal.md`](docs/adr/0040-markdown-extraction-seed-terminal.md)

- **`IPageCache` — a cache-read seam on the page loader, with the firecrawl-shaped `WithMaxAge(TimeSpan)` one-liner (ADR-0041).** A new public `IPageCache` (`TryReadAsync` / `WriteAsync`, keyed on `(url, pageType)`) sits beside `PageLoader` as a cache-aside collaborator; `InMemoryPageCache(TimeSpan maxAge)` ships as the firecrawl-shaped TTL adapter, `NullPageCache` is the no-op default. Enables iterative crawl development without re-fetching and gives the router (ADR-0046) and self-heal (ADR-0047) free re-reads. Additive — no Tier-1 break.
  [`docs/adr/0041-page-cache-seam.md`](docs/adr/0041-page-cache-seam.md)

- **`ISiteMapper` — URL discovery via `sitemap.xml` ∪ root-page links (ADR-0042).** A new public `ISiteMapper` + default `SiteMapper` adapter parses `robots.txt` for `Sitemap:` lines, recurses one level of sitemap-indexes, extracts root-page `<a href>`s, and returns a deduped ordered URL list — without spending the Crawl/visited-link/page-processor pipeline on a one-HTTP-request operation. `ScraperEngineBuilder.MapAsync(url, options?)` is the one-liner; `MapOptions` exposes `MaxUrls` / `Search` / `AllowOffsite` knobs. Additive.
  [`docs/adr/0042-site-mapper.md`](docs/adr/0042-site-mapper.md)

- **`WebReaper.Cli` — the AOT single-binary primitive agent surface, plus a bundled Agent Skill (ADR-0043).** A new AOT-clean executable (`PublishAot=true`, zero NuGet deps beyond the BCL, hand-rolled ~120-line arg parser) ships `webreaper scrape <url>` (defaults to Markdown, `--schema <path>` switches to typed JSON), `webreaper map <url>`, `webreaper init` (writes an embedded `SKILL.md` into `.claude/skills/webreaper/`), and `webreaper version`. The CLI is the primitive; Skill and MCP are adapters over it. Additive.
  [`docs/adr/0043-cli-and-agent-skill.md`](docs/adr/0043-cli-and-agent-skill.md)

- **`WebReaper.AI` — an LLM-backed `IContentExtractor` satellite bound to `Microsoft.Extensions.AI.Abstractions` (ADR-0044).** New satellite shipping `LlmContentExtractor` (Markdown pre-clean by default, deterministic `Temperature = 0`, `MaxTokens = 4096`), a `Schema` → JSON Schema bridge, and `WithLlmExtractor(IChatClient, LlmExtractorOptions?)`. BYO model — the consumer brings their own `IChatClient`; core stays dependency-light and AOT-clean per ADR-0009 quarantine. Additive.
  [`docs/adr/0044-llm-extractor-satellite.md`](docs/adr/0044-llm-extractor-satellite.md)

- **`[ScrapeSchema]` — a Roslyn source generator emitting `Schema` from attributed POCOs (ADR-0045).** Two new packages — `WebReaper.Extraction.Attributes` (the `[ScrapeSchema]` / `[ScrapeField]` markers + `SchemaFieldType`) and `WebReaper.Extraction.Generators` (an `IIncrementalGenerator`). A `partial` POCO with attributed properties gets a compile-time `static Schema Schema` and a reflection-free `static Materialize(JsonObject)`, both AOT-clean. v1 ships the common case (single-level, primitive fields, `List<T>` of primitives); nested `[ScrapeSchema]` POCOs are explicitly deferred. Additive.
  [`docs/adr/0045-scrape-schema-source-generator.md`](docs/adr/0045-scrape-schema-source-generator.md)

- **`ExtractionRouter` — deterministic-first → fallback composition on the `IContentExtractor` seam (ADR-0046).** A new public `ExtractionRouter(primary, fallback, isValid?, logger?)` is itself an `IContentExtractor` — no seam-of-a-seam. Runs the deterministic fold, validates via `SchemaSatisfiedValidator` (a required leaf is missing iff absent or string-empty / list-empty), falls back to e.g. the LLM extractor only when the cheap path fails. `ScraperEngineBuilder.WithFallbackExtractor` is the sugar; `WebReaper.AI` ships `WithLlmFallback`. Additive.
  [`docs/adr/0046-extraction-router.md`](docs/adr/0046-extraction-router.md)

- **`SelfHealingContentExtractor` — LLM proposes selectors, the fold validates, the schema-cache persists the fix (ADR-0047).** New public `ISelectorRepairer` seam plus a `SelfHealingContentExtractor` wrapper: on a deterministic-fold failure, the repairer proposes new selectors, the fold re-runs with the patched `Schema`, and if it validates, the patch is cached (reference-identity, per-crawl in-memory) so every subsequent page runs deterministic again — no recurring LLM cost. `WebReaper.AI` ships `LlmSelectorRepairer` + `WithLlmSelfHealing`. Additive.
  [`docs/adr/0047-self-healing-selectors.md`](docs/adr/0047-self-healing-selectors.md)

- **`ChangeTrackingProcessor` — snapshot Markdown per URL, emit `change_status` on the page-processor pipeline (ADR-0048).** A new `IPageProcessor` (ADR-0038) hashes each page's Markdown extraction (SHA-256, robust to template noise), looks up the prior hash in a new `IChangeStore` seam (`InMemoryChangeStore` default), and annotates the record with `change_status` (`new` / `same` / `changed`) plus `previous_hash`. `ScraperEngineBuilder.WithChangeTracking(IChangeStore? = null)` is the sugar. Additive; `removed` detection and diff text are deferred.
  [`docs/adr/0048-change-tracking-processor.md`](docs/adr/0048-change-tracking-processor.md)

- **`WebReaper.Mcp` — MCP server satellite exposing scrape/map/extract as MCP tools (ADR-0049).** New Exe satellite over the `ModelContextProtocol` C# SDK with stdio transport, exposing three `[McpServerTool]` methods that wrap the existing library API — for MCP-only clients (Cursor, ChatGPT Desktop, Copilot Studio) that can't reach the CLI. Thin facade; pre-1.0 SDK churn quarantined per ADR-0009. Additive.
  [`docs/adr/0049-mcp-server-satellite.md`](docs/adr/0049-mcp-server-satellite.md)

- **`PageAction.SemanticAct(intent)` — natural-language page actions; LLM resolves once, deterministic thereafter (ADR-0050).** A seventh closed-sum `PageAction` arm carrying an intent string ("click sign in") instead of a CSS selector. New public `IActionResolver` seam + `ScraperEngineBuilder.WithActionResolver(...)`; the `WebReaper.AI` satellite ships `LlmActionResolver` + `WithLlmActionResolver(chatClient)`. The Puppeteer transport resolves the intent on the first dynamic page, dispatches the concrete arm, and caches the resolution per crawl by intent string — every subsequent same-intent page dispatches the cached arm with no LLM call. The cache lives in core (`SemanticActCoordinator`), unit-testable without an `IPage`. Same proposer-validator pattern as the extraction router (ADR-0046) and self-healing extractor (ADR-0047), generalised from extraction to actions — self-heal stops being one feature and becomes a *project-level pattern*. **Narrow breaking edge:** `ScraperEngineBuilder.WithLoadTransport`'s factory delegate widens from 3 to 4 arguments (the fourth is `IActionResolver`); the in-tree `WebReaper.Puppeteer` satellite is updated in lockstep. A `SemanticAct` in the config without a registered resolver logs a warning at `BuildAsync` and throws `SemanticActResolutionException` on the first dispatch.
  [`docs/adr/0050-semantic-page-actions.md`](docs/adr/0050-semantic-page-actions.md)

### Licence (ADR-0017)

- **WebReaper relicenses GPL-3.0-or-later → MIT.** Every NuGet package in the
  release ships `<PackageLicenseExpression>MIT</PackageLicenseExpression>`
  (core + every satellite, including the four new AI-native satellites). The
  funnel's last legal-adoption-friction edge for downstream consumers and
  SaaS integrators is gone. Full gates, contributor analysis, and history
  rewrite (Deloitte → personal email on 41 commits) recorded in
  [`docs/adr/0017-relicense-gpl-mit.md`](docs/adr/0017-relicense-gpl-mit.md);
  the rewrite's backup branches `origin/pre-email-rewrite-master` and
  `origin/pre-email-rewrite-ai-native-wave` are deletable once the owner is
  satisfied (~30 days).

## 9.0.0 — The public surface is the documented contract (breaking)

The core public API is now exactly the contract — no wider, no narrower —
documented to the bar the codebase already set, and enforced. ADR-0023 drew
the line with the deletion test (named by a documented consumer / inherited by
a satellite / part of the taught fluent API ⇒ public; reached only through a
builder method and named by nobody ⇒ implementation). The ~552-warning CS1591
backlog the satellite csprojs deliberately kept *visible* is closed: every
Tier-1 type carries real intent-revealing XML doc, every Tier-2 implementation
type is now `internal`, and `<WarningsAsErrors>CS1591</WarningsAsErrors>` makes
the documented surface non-regressing (a new undocumented public member fails
the build). Rationale, the deletion-test line, the rejected alternatives (the
shallow factory; document-everything; the non-breaking 8.1.0/9.0.0 split) and
the staged burndown:
[`docs/adr/0023-core-doc-contract.md`](docs/adr/0023-core-doc-contract.md).

The satellite csprojs' "core keeps CS1591 visible — a live doc backlog"
rationale is updated in place to point here: core CS1591 is now a
contract-enforced zero, not an open backlog.

### Breaking changes

- **The Tier-2 implementation adapters are now `internal`.** The concrete
  types reached only via the fluent builder (or core-internally) are no longer
  public: the `File*` / `InMemory*` storage·scheduler·tracker·blob leaves
  (`FileScheduler`, `FileScraperConfigStorage`, `FileCookieStorage`,
  `FileVisitedLinkedTracker`, `FileBlobStore`, `InMemoryBlobStore`,
  `InMemoryCookieStorage`), the sinks (`ConsoleSink`, `CsvFileSink`,
  `JsonLinesFileSink`, `BufferedFileSink`, `CsvFormat`, `JsonLinesFormat`),
  the parsers/loaders (`AngleSharpContentParser`, `JsonContentParser`,
  `XPathContentParser`, `LinkParserByCssSelector`, `PageLoader`,
  `HttpPageLoadTransport`, `BrowserNotConfiguredPageLoadTransport`), the
  crawl internals (`Spider`, `CrawlStep`, `InMemoryOutstandingWorkLatch`),
  `ValidatedProxyProvider`, `Executor`, `ColorConsoleLogger`, and the
  `LogMethodDuration` / `LogInvocationCount` helpers `Timer` / `Counter`.
- **`ScraperEngine`'s constructor is `internal`.** The class stays public (you
  hold it and call `RunAsync`); only `new ScraperEngine(...)` is gone —
  `ScraperEngineBuilder.BuildAsync()` is the construction contract (the
  `internal SpiderBuilder` / `BuildSpider()` precedent).
- **No shipped package is affected.** A repo-wide sweep confirmed no
  satellite-prod / Example / Misc code names a Tier-2 type; satellites bind
  the Tier-1 interfaces and inherit the Tier-1 payload-shell bases
  (`CookieStore` / `ScraperConfigStore`). `[InternalsVisibleTo]` targets the
  test assemblies only — never a NuGet package.
- **Kept public on purpose:** the fluent builders, every `*/Abstract` seam
  interface, the `Domain` model, `WebReaperJson`, `LoggerExtensions`,
  `ScraperEngine` (the type), the in-memory default adapters the
  distributed-worker pattern wires by hand, the `CookieStore` /
  `ScraperConfigStore` satellite-inheritance bases, `StaticProxySource` /
  `HttpProxyValidator` (the only built-in `WithValidatedProxies` inputs), and
  **`SchemaContentParser<TNode>`** — the ADR-0002 custom-backend reuse vehicle.

### Migration

A fluent-API consumer (`new ScraperEngineBuilder()…BuildAsync()` /
`.RunAsync()`), a custom seam implementer (`IScraperSink`, `IScheduler`,
`ISchemaBackend<TNode>` + `SchemaContentParser<TNode>`, …), and the
distributed-worker pattern all need **no changes** — every type they touch
stayed public. The only break is code that constructed a core *implementation*
adapter by name (e.g. `new ConsoleSink()`, `new FileScheduler(...)`): switch
to the builder method that wired it (`.WriteToConsole()`,
`.WithTextFileScheduler(...)`) or to the interface. No behavioural change —
this is visibility + documentation only (zero IL delta in the documented
paths; `WebReaper.AotSmokeTest` still publishes Native-AOT zero-warning).

## 8.0.0 — Crawl driver + Outstanding-work latch; the per-Job shell is a value, not a thrower (breaking)

The per-Job `Spider` shell stops leaking its result through side channels and
stops throwing to terminate. `ISpider.CrawlAsync` now returns a closed
`JobReport` (the ADR-0001 `CrawlOutcome` + the loaded document); the shell is
reduced to load → Crawl step → report. The **Crawl driver** — in-process
`ScraperEngine` or the distributed worker — owns what the shell used to: the
visited-link tracker, the crawl-limit stop, sink fan-out, and the
`PostProcessor` / `ScrapedData` callbacks. The crawl limit is now a value the
driver checks (`Scheduler.Complete()`), no longer a `PageCrawlLimitException`
thrown through the fault-retry policy. Rationale, the rejected alternatives
(durable-workflow coordinator, emergent queue-drain, exact distributed limit)
and the staged design:
[`docs/adr/0022-crawl-driver-and-outstanding-work-latch.md`](docs/adr/0022-crawl-driver-and-outstanding-work-latch.md)
and [`research/distributed-crawl-termination.md`](research/distributed-crawl-termination.md).

**Release packaging (lockstep).** This is a core-major release wave: core and
all six satellites (`Cosmos`, `Mongo`, `Redis`, `AzureServiceBus`, `Puppeteer`,
`Sqlite`) are republished at `8.0.0` together, so a consumer never sees a
`WebReaper 8.0.0` + satellite-`7.x` skew and every satellite declares a
`WebReaper >= 8.0.0` dependency. Only `WebReaper.Redis` changed functionally
(the distributed Outstanding-work latch + the atomic-`SADD` `TryAdd`); the
other satellites are unchanged, rebuilt against core 8.0.0 for graph
coherence (`docs/RELEASE-RUNBOOK.md` lockstep selection).

The visited-link tracker becomes the single **idempotency authority**:
`IVisitedLinkTracker.TryAddVisitedLinkAsync` is an atomic test-and-set
(default-interface-method; `InMemoryVisitedLinkTracker` is a lock-free CAS,
`RedisVisitedLinkTracker` an atomic `SADD`). Termination detection is one
`IOutstandingWorkLatch` seam — a unit-credit counter that trips exactly once
when all work has drained, with an in-memory `Interlocked` adapter and a
distributed Redis adapter (atomic `INCRBY`/`DECRBY` + a `SET NX` one-shot
fence). `Examples/WebReaper.AzureFuncs` is now a real distributed Crawl
driver: it never throws to terminate, so the queue is no longer poisoned at
the crawl-limit boundary.

Closed by construction: the retry-amplified limit exception, the racy
discovery dedup, and the distributed poison message. The fluent builder API
is unchanged — `.PageCrawlLimit(...)`, `.Subscribe(...)`, `.PostProcess(...)`
and `.StopWhenAllLinksProcessed()` keep their signatures and behaviour; only
their internal wiring moved to the driver.

**Build hygiene (non-breaking).** The obsolete, *inert* `ServicePointManager`
calls in the HTTP transport were removed — `ServicePointManager` does not
affect `HttpClient`/`SocketsHttpHandler`, and the connection limit and
cert-bypass already live on the per-request `SocketsHttpHandler`, so there is
no behavioural change. The broken/ambiguous XML-doc crefs and bad paramrefs
that shipped in the package's IntelliSense XML were fixed. The deliberately
visible core CS1591 doc backlog is intentionally left as-is (it is a tracked
signal, not noise — see the satellite csproj rationale).

### Breaking changes

- **`ISpider.CrawlAsync` returns `Task<JobReport>`**, not `Task<List<Job>>`.
  A direct `ISpider` consumer (the `BuildSpider()` distributed-worker pattern)
  reads `report.Outcome.NextJobs` for child jobs and matches
  `report.Outcome is CrawlOutcome.Parsed` for a parsed page; sink fan-out and
  visited-link tracking are now the caller's (driver's) job.
- **`ScrapedData` / `PostProcessor` moved off the concrete `Spider`** to the
  Crawl driver. The public `ScraperEngineBuilder.Subscribe(...)` /
  `.PostProcess(...)` surface is unchanged and now wires onto the driver.
- **`PageCrawlLimitException` is removed.** The crawl limit is a value-driven
  stop, never an exception; remove any `catch (PageCrawlLimitException)`.
- **`Spider`'s constructor changed** (reduced to `ICrawlStep`, `IPageLoader`,
  `IScraperConfigStorage`); `ScraperEngine`'s constructor gained the
  visited-link tracker, sinks, optional callbacks and an optional
  `IOutstandingWorkLatch` (in-memory default). Both are normally built via
  `ScraperEngineBuilder` — no change for fluent-API consumers.
- **No compat shell.** A `List<Job>`-returning forwarder would reinstate the
  side channels this change removes (the ADR-0009 precedent); the break is
  announced here and in ADR-0022, not silent.
- **Builder argument validation is now fail-fast.** Public builder entry
  points that take a free-form string reject `null`/empty/whitespace *at the
  call that introduced it* instead of failing much later (at parse time, at
  first file I/O, or — for start URLs — as an engine that quietly crawled
  nothing): `ConfigBuilder.Build()` now requires a non-empty start-URL set and
  its message is plural; `Follow`/`FollowWithBrowser`/`Paginate`/
  `PaginateWithBrowser` reject a blank selector; the file-backed
  `ScraperEngineBuilder` methods (`WriteToCsvFile`/`WriteToJsonFile`/
  `TrackVisitedLinksInFile`/`WithFileConfigStorage`/`WithFileCookieStorage`/
  `WithTextFileScheduler`) reject a blank path; and `PageActionBuilder`'s
  `Repeat`/`RepeatWithDelay`/`RepeatAndWaitForNetworkIdle` throw a clear
  `InvalidOperationException` when called before any action (was a bare
  `ArgumentOutOfRangeException`). Correct code is unaffected — only
  previously-invalid input now throws; a major is the right window for the
  few cases that were silently accepted before.

### Migration

A fluent-API consumer (`new ScraperEngineBuilder()…BuildAsync()` /
`.RunAsync()`) needs **no changes** — the builder surface and behaviour are
preserved. A direct `ISpider` / `BuildSpider()` consumer updates to the
`JobReport` shape (re-enqueue `report.Outcome.NextJobs`; fan a
`CrawlOutcome.Parsed` page out to its sink itself) and drops any
`PageCrawlLimitException` handling; the rewritten
`Examples/WebReaper.AzureFuncs` is the reference distributed Crawl-driver
adapter. The one behavioural caveat for a fluent consumer: builder method
*signatures* are unchanged, but a call that previously passed a blank
string / empty start-URL set (or `Repeat*` before any action) now throws at
that call — pass valid input.

## 7.1.0 — WebReaper.Sqlite satellite: opt-in robust-local durable scheduler & tracker (additive)

New satellite package **WebReaper.Sqlite** — a local durable scheduler and
visited-link tracker backed by an embedded SQLite store via
`Microsoft.Data.Sqlite`. "Resume" is a `SELECT … WHERE consumed = 0` over an
indexed table: `FileScheduler`'s append-only job file + sidecar position file
+ `O(skip N)` line cursor (and the cursor↔job-file desync failure mode) are
gone for the consumer who opts in. Rationale, the satellite-not-core
constraint, and the considered options:
[`docs/adr/0012-sqlite-embedded-store-satellite.md`](docs/adr/0012-sqlite-embedded-store-satellite.md).

This is **additive** — nothing is removed or changed in core or the existing
satellites. The core file scheduler and visited-link tracker are
byte-unchanged and remain the zero-dependency local default; SQLite is the
opt-in robust-local tier between them and the distributed Redis / Azure
Service Bus satellites.

- `ScraperEngineBuilder.WithSqliteScheduler(databasePath, dataCleanupOnStart?, logger?)`
  over the public `WithScheduler` seam; `SqliteScheduler : IScheduler`.
- `ScraperEngineBuilder.TrackVisitedLinksInSqlite(databasePath, dataCleanupOnStart?)`
  over the public `WithLinkTracker` seam; `SqliteVisitedLinkTracker :
  IVisitedLinkTracker`. The `visited(url PRIMARY KEY)` table *is* the set —
  no in-memory mirror (a deliberate ADR-0012 deviation from the file
  tracker, mirroring the Redis tracker).
- The `Job` payload uses the same `WebReaperJson` grammar as the core file
  scheduler and the Redis scheduler (ADR-0008) — full type fidelity.
- Satellite per ADR-0009 / ADR-0012: core does not reference
  `Microsoft.Data.Sqlite`, so the native `e_sqlite3` (SQLitePCLRaw) graph
  stays off the dependency-light, Native-AOT-zero-warning core. Like the
  other satellites it is deliberately not marked `IsAotCompatible`.
- Versioned `7.1.0`: a new satellite added after the `7.0.0` satellite wave;
  it depends on `WebReaper` core `7.0.0`.

ADR-0012 itself carried a one-line correction (landed with the first
implementation slice, called out loud): its Mechanism section had wrongly
said `FileScheduler` writes its position file *after* the yield — the whole
`IScheduler` family is claim-before-yield / at-most-once for the in-flight
job, and `SqliteScheduler` matches it. Decision and shape unaffected.

## 7.0.0 — Satellite adapter packages; dependency-light core (breaking)

Heavy third-party adapters move out of the core `WebReaper` package into
per-technology satellite packages, wired through the builder's public
registration seam. Rationale, design, and the deliberate clean-cut (no compat
shell): [`docs/adr/0009-registration-seam-and-satellite-adapters.md`](docs/adr/0009-registration-seam-and-satellite-adapters.md).

This release lands the full ADR-0009 satellite set: **Cosmos**, **Mongo**,
**Redis**, **Azure Service Bus** and **Puppeteer**. The core `WebReaper`
package no longer references any of those SDKs — a plain HTTP→file crawl
pulls none of them.

It also closes the ADR-0008-named JSONPath follow-up: `JsonSchemaBackend`'s
Newtonsoft `JToken` JSONPath cursor — the last Newtonsoft reach in core — is
migrated to an in-repo JSONPath-subset evaluator over
`System.Text.Json.Nodes.JsonNode`. The supported dialect is preserved exactly
(optional `$`/`$.` root, `.`-separated property paths, trailing `[*]` array
wildcard — the whole surface the `Schema` model drives, pinned by the JSON
test corpus). With `CookieStore` already on System.Text.Json, **core is now
entirely Newtonsoft-free**: the `Newtonsoft.Json` `PackageReference` is
dropped and the *whole* core (not just a scoped path) publishes Native-AOT
zero-warning — verified by `WebReaper.AotSmokeTest`, now extended to exercise
the JSON backend. Rationale and the doc-lag correction:
[`docs/adr/0008-system-text-json-typed-pipeline.md`](docs/adr/0008-system-text-json-typed-pipeline.md).

### Breaking changes

- **`WriteToCosmosDb` moved to the `WebReaper.Cosmos` package.** It is now an
  extension method over `ScraperEngineBuilder`'s public `AddSink` registration
  seam, not a core builder method. `SpiderBuilder.WriteToCosmosDb` is removed.
- **`CosmosSink` moved**: `WebReaper.Sinks.Concrete` → namespace and package
  `WebReaper.Cosmos`.
- **Core no longer references `Microsoft.Azure.Cosmos`.** A core-only consumer
  no longer pulls Cosmos (or its Newtonsoft + native ServiceInterop graph).
- **No compat forwarders.** A forwarder would still reference the package and
  defeat the dependency-light core (ADR-0009 SemVer).
- `WriteToCosmosDb` no longer auto-uses the builder's logger; it takes an
  optional `ILogger` argument (defaults to `NullLogger`).
- **`WriteToMongoDb`, `WithMongoDbConfigStorage`, `WithMongoDbCookieStorage`
  moved to the `WebReaper.Mongo` package.** They are now extension methods
  over `ScraperEngineBuilder`'s public `AddSink` / `WithConfigStorage` /
  `WithCookieStorage` registration seams. `SpiderBuilder.WithMongoDbConfigStorage`
  and `SpiderBuilder.WithMongoDbCookieStorage` are removed.
- **Mongo adapter types moved** to namespace and package `WebReaper.Mongo`:
  `MongoDbSink` (was `WebReaper.Sinks.Concrete`), `MongoDbScraperConfigStorage`
  (was `WebReaper.ConfigStorage.Concrete`), `MongoDbCookieStorage` (was
  `WebReaper.Core.CookieStorage.Concrete`), `MongoBlobStore` (was
  `WebReaper.DataAccess`).
- **Core no longer references `MongoDB.Driver`.** A core-only consumer no
  longer pulls it — and the transitive `SharpCompress` `GHSA-6c8g-7p36-r338`
  audit-suppression moves out of core with it (now in `WebReaper.Mongo`).
- The three Mongo builder extensions no longer auto-use the builder's logger;
  each takes an optional `ILogger` argument (defaults to `NullLogger`).
- **`WithRedisScheduler`, `TrackVisitedLinksInRedis`, `WriteToRedis`,
  `WithRedisConfigStorage`, `WithRedisCookieStorage` moved to the
  `WebReaper.Redis` package.** They are now extension methods over
  `ScraperEngineBuilder`'s public `WithScheduler` / `WithLinkTracker` /
  `AddSink` / `WithConfigStorage` / `WithCookieStorage` registration seams.
  `SpiderBuilder.WriteToRedis` / `WithRedisConfigStorage` /
  `WithRedisCookieStorage` are removed.
- **Redis adapter types moved** to namespace and package `WebReaper.Redis`:
  `RedisScheduler` (was `WebReaper.Core.Scheduler.Concrete`),
  `RedisVisitedLinkTracker` (was `WebReaper.Core.LinkTracker.Concrete`),
  `RedisSink` (was `WebReaper.Sinks.Concrete`), `RedisScraperConfigStorage`
  (was `WebReaper.ConfigStorage.Concrete`), `RedisCookieStorage` (was
  `WebReaper.Core.CookieStorage.Concrete`), `RedisBlobStore` and
  `RedisConnectionPool` (were `WebReaper.DataAccess`).
- **Core no longer references `StackExchange.Redis`.** A core-only consumer
  no longer pulls it. ADR-0005's one-`ConnectionMultiplexer`-per-connection-string
  invariant is preserved: `RedisConnectionPool` moves whole and stays the
  single resolver every Redis adapter in the package goes through.
- The Redis builder extensions that took a logger no longer auto-use the
  builder's; each takes an optional `ILogger` argument (defaults to
  `NullLogger`).
- **`WithAzureServiceBusScheduler` moved to the `WebReaper.AzureServiceBus`
  package.** It is now an extension method over `ScraperEngineBuilder`'s public
  `WithScheduler` registration seam, not a core builder method. There was no
  `SpiderBuilder` equivalent to remove.
- **`AzureServiceBusScheduler` moved** to namespace and package
  `WebReaper.AzureServiceBus` (was `WebReaper.Core.Scheduler.Concrete`).
- **Core no longer references `Azure.Messaging.ServiceBus`.** A core-only
  consumer no longer pulls it. `WithAzureServiceBusScheduler` took no logger,
  so its signature is unchanged.
- **The headless-browser page loader moved to the `WebReaper.Puppeteer`
  package; the core default page loader is now HTTP-only.**
  `BrowserPageLoadTransport` moved (`WebReaper.Core.Loaders.Concrete` →
  namespace and package `WebReaper.Puppeteer`); `CookieExtensions` /
  `ToPuppeteerCookies` moved (`WebReaper.Extensions` → namespace and package
  `WebReaper.Puppeteer`).
- **Core no longer references `PuppeteerSharp` / `PuppeteerExtraSharp`.** A
  core-only consumer no longer pulls them or the Chromium provisioning path,
  and the ADR-0008-named `BrowserPageLoadTransport` `Assembly.Location` IL3000
  trim/AOT finding leaves core with it (now in `WebReaper.Puppeteer`).
- **New `WithLoadTransport` registration seam on `ScraperEngineBuilder`.** It
  takes a factory —
  `Func<ICookiesStorage, IProxyProvider?, ILogger, IPageLoadTransport>` —
  invoked at build time with the builder's resolved cookie storage, optional
  proxy provider and logger (a deliberate refinement of ADR-0009's stated
  `WithLoadTransport(IPageLoadTransport)`; see that ADR's Deliberate
  consequences note). ADR-0004's one-`IPageLoader` /
  two-`IPageLoadTransport` dispatcher is unchanged — only the default
  composition of the Dynamic slot moved out of core.
- **`GetWithBrowser` / `FollowWithBrowser` / `PaginateWithBrowser` still
  compile, but Dynamic pages now require `WebReaper.Puppeteer`.** With no
  browser transport registered a Dynamic load throws an actionable
  `InvalidOperationException` (`BrowserNotConfiguredPageLoadTransport`)
  pointing at the package and `.WithPuppeteerPageLoader()`, instead of a
  silent default. `.WithPuppeteerPageLoader()` is parameterless and — because
  the seam is a factory — reproduces the pre-7.0 behaviour exactly: the one
  shared cookie container (issue #26) and the optional proxy applied the
  browser's own way.
- **`SpiderBuilder` is now `internal`; the bare-spider seam is
  `ScraperEngineBuilder.BuildSpider()`.** The ADR-0009 capstone: the public
  registration seam lives *only* on `ScraperEngineBuilder`, and
  `SpiderBuilder`'s duplicate public surface is gone. The distributed-worker
  pattern (crawl one queued `Job`, re-enqueue its children — see
  `Examples/WebReaper.AzureFuncs`) gets a bare `ISpider` from the new public
  `ScraperEngineBuilder.BuildSpider()` instead of `new SpiderBuilder()…Build()`.
  Unlike `BuildAsync()`, `BuildSpider()` does not build or persist a
  `ScraperConfig`, so it does not require `Get`/`Parse` — the worker's config
  is persisted separately and read from storage at crawl time.

### Why

- Dependency-light core: a plain HTTP→file crawl stops transitively pulling
  Cosmos + Newtonsoft + native interop, `MongoDB.Driver` + its transitive
  SharpCompress CVE, `StackExchange.Redis`, `Azure.Messaging.ServiceBus`, and
  `PuppeteerSharp` + the Chromium provisioning path (carrying the ADR-0008
  `BrowserPageLoadTransport` IL3000 finding out of core).
- The builder deepens into a small public registration seam; per-adapter
  `WriteToX` sugar ships with its adapter. See ADR-0009.

### Migration

- Add the package: `dotnet add package WebReaper.Cosmos`.
- Add `using WebReaper.Cosmos;` wherever you call `.WriteToCosmosDb(...)` or
  reference the `CosmosSink` type. `WriteToCosmosDb`'s existing arguments are
  unchanged (an optional `ILogger` is appended).
- Add the package: `dotnet add package WebReaper.Mongo`.
- Add `using WebReaper.Mongo;` wherever you call `.WriteToMongoDb(...)`,
  `.WithMongoDbConfigStorage(...)`, `.WithMongoDbCookieStorage(...)` or
  reference the `MongoDbSink` / `MongoDbScraperConfigStorage` /
  `MongoDbCookieStorage` / `MongoBlobStore` types. Existing arguments are
  unchanged (an optional `ILogger` is appended).
- Add the package: `dotnet add package WebReaper.Redis`.
- Add `using WebReaper.Redis;` wherever you call `.WithRedisScheduler(...)`,
  `.TrackVisitedLinksInRedis(...)`, `.WriteToRedis(...)`,
  `.WithRedisConfigStorage(...)`, `.WithRedisCookieStorage(...)` or reference
  the `RedisScheduler` / `RedisVisitedLinkTracker` / `RedisSink` /
  `RedisScraperConfigStorage` / `RedisCookieStorage` / `RedisBlobStore` /
  `RedisConnectionPool` types. Existing arguments are unchanged (an optional
  `ILogger` is appended where one was passed).
- Add the package: `dotnet add package WebReaper.AzureServiceBus`.
- Add `using WebReaper.AzureServiceBus;` wherever you call
  `.WithAzureServiceBusScheduler(...)` or reference the
  `AzureServiceBusScheduler` type. Its arguments are unchanged.
- Add the package: `dotnet add package WebReaper.Puppeteer`.
- Add `using WebReaper.Puppeteer;` and call `.WithPuppeteerPageLoader()` on
  the builder wherever you scrape Dynamic pages (`GetWithBrowser` /
  `FollowWithBrowser` / `PaginateWithBrowser`), or wherever you reference the
  `BrowserPageLoadTransport` type or `CookieContainer.ToPuppeteerCookies(...)`.
  `.WithPuppeteerPageLoader()` takes no arguments and reproduces the pre-7.0
  default behaviour. A core-only (HTTP) crawl needs no change.
- If you constructed `new SpiderBuilder()…Build()` directly (the
  distributed-worker pattern), switch to
  `new ScraperEngineBuilder()…BuildSpider()` — the same `WithLogger` /
  `WithLinkTracker` / `AddSink` / etc. configuration, returning the same
  `ISpider`. Fluent `ScraperEngineBuilder` consumers need no change.
- If your code used `Newtonsoft.Json` and relied on getting it *transitively*
  through the `WebReaper` package, add an explicit
  `<PackageReference Include="Newtonsoft.Json" .../>` — core no longer
  references it. WebReaper's own APIs are `System.Text.Json` throughout, so a
  consumer that does not use Newtonsoft itself needs no change.

No code or API change, listing only: each satellite package
(`WebReaper.Cosmos` / `.Mongo` / `.Redis` / `.AzureServiceBus` / `.Puppeteer`)
now ships a focused README, the shared WebReaper icon, and release notes in its
`.nupkg`, so its nuget.org page renders like the core package's instead of
blank.

No code or API change, IntelliSense only: the core package now ships its XML
documentation as `lib/<tfm>/WebReaper.xml` (it previously shipped an `API.xml`
the IDE never resolved next to `WebReaper.dll`, so consumers got no doc
tooltips); the `DocumentationFile` redirect that also wrote a build artifact
into the tracked tree is gone. Each satellite now generates and ships its own
XML doc too, with the builder-extension API (`WriteToCosmosDb`,
`WithRedisScheduler`, …) documented; the moved adapter classes remain
undocumented by design (CS1591 suppressed in the satellites only).

## 6.0.0 — System.Text.Json typed pipeline (breaking, AOT-clean)

The extraction and persistence pipeline moved off `Newtonsoft.Json` +
`TypeNameHandling.Auto` to `System.Text.Json` source-gen with a typed
`JsonObject` terminal. Rationale, design, and bounded scope:
[`docs/adr/0008-system-text-json-typed-pipeline.md`](docs/adr/0008-system-text-json-typed-pipeline.md)
(supersedes the serialization grammar of ADR-0002/0003; closes the ADR-0005
`RedisScheduler` `Job` round-trip).

### Breaking changes

- **`ParsedData.Data`** is now `System.Text.Json.Nodes.JsonObject` (was
  `Newtonsoft.Json.Linq.JObject`).
- **`IFileSinkFormat.Header(JsonObject)` / `FormatRow(JsonObject)`** (was
  `JObject`). Observable file content (CSV header/rows, JSON-lines) is
  unchanged.
- **`PostProcess(Func<Metadata, JsonObject, Task>)`** on `ScraperEngineBuilder`
  / `SpiderBuilder` (was `JObject`).
- **`IContentParser` removed.** The Newtonsoft `JObject`-returning
  `ParseAsync` is gone. Use `IJsonContentParser.ParseToJsonAsync` →
  `JsonObject`. The built-in parsers (`AngleSharpContentParser`,
  `JsonContentParser`, `XPathContentParser`, `SchemaContentParser<TNode>`)
  implement `IJsonContentParser`; `WithContentParser` now takes
  `IJsonContentParser`.
- **Persisted/wire format changed.** Config, every `Job` (Redis, Azure Service
  Bus, File schedulers), and cookies now serialize via System.Text.Json
  source-gen (no `TypeNameHandling`). Polymorphic `PageAction.Parameters`,
  the `ImmutableQueue<LinkPathSelector>` chain, and `Schema`/`SchemaElement`
  round-trip via dedicated converters. **Clear distributed job queues and
  stored scraper config on upgrade** — old Newtonsoft-format payloads are not
  read by the new grammar.

### Why

- Removes Newtonsoft's reflection / `TypeNameHandling` — a *documented* bug
  class (the ADR-0003 file-adapter serialize-`Auto`/deserialize-defaults
  asymmetry; the ADR-0005 `RedisScheduler` `Job` asymmetry), now closed
  uniformly across all schedulers.
- AOT-clean typed pipeline: the Newtonsoft-free configuration
  (markup/CSS/XPath + STJ config/schedulers/sinks) publishes Native-AOT with
  zero trim/AOT warnings (verified by a CI `WebReaper.AotSmokeTest`); the
  library declares `IsAotCompatible`.
- Smaller, single-file, no-runtime-install deploy footprint.

### Migration

- Replace `JObject`/`JToken` in your `PostProcess`, custom sink, or custom
  parser code with `System.Text.Json.Nodes`: `obj["k"]!.GetValue<T>()`,
  `obj["k"]!.ToString()`, `obj["k"]!.AsArray()`, `JsonNode.DeepEquals(...)`.
- `parser.ParseAsync(...)` → `parser.ParseToJsonAsync(...)`.
- Drain Redis/Azure Service Bus job queues and delete stored config produced
  by ≤ 5.1.0 before running 6.0.0.

### Not removed (still Newtonsoft, opt-in, AOT-dirty only if used)

- **JSON-endpoint scraping** (`JsonContentParser` / `JsonSchemaBackend`): the
  JSONPath scope cursor is Newtonsoft `JToken` — System.Text.Json has no
  JSONPath. Named ADR-0008 follow-up.
- **`CosmosSink`**: the Cosmos SDK is itself Newtonsoft-coupled. See ADR-0008
  Bounded scope.

A consumer that uses neither still gets a fully AOT-clean publish (unreached
Newtonsoft is trimmed away).

## 5.1.0

- XPath selector backend (`AngleSharpXPathSchemaBackend`), discussion #17,
  ADR-0007.

## 5.0.0

- One page-loader seam with internal transports (ADR-0004); one keyed blob
  store + payload shells (ADR-0003); `RedisConnectionPool` (ADR-0005);
  buffered file-sink drain (ADR-0006).
