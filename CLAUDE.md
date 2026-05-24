# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

WebReaper — declarative, parallel web scraper/crawler library for .NET, published as the `WebReaper` NuGet package. Library lives in `WebReaper/`; `Examples/` and `Misc/` are consumers, not packaged.

## Commands

```bash
dotnet build WebReaper.sln                  # build all
dotnet test                                  # all tests (xUnit)
dotnet test WebReaper.Tests/WebReaper.UnitTests/WebReaper.UnitTests.csproj   # unit only (fast, offline)
dotnet test --filter "FullyQualifiedName~SimpleTest"   # single test by name
dotnet run --project Examples/WebReaper.ConsoleApplication   # run an example scraper
```

### Test reality

- **Unit tests** (`WebReaper.Tests/WebReaper.UnitTests`) — offline, parse `TestData/TestPage.html`. Use these for fast iteration.
- **Integration tests** (`WebReaper.Tests/WebReaper.IntegrationTests`) — hit the **live** site `alexpavlov.dev`, launch real Puppeteer/Chromium, and assert after fixed `Task.Delay` (up to 25s). Slow and network-flaky by design; failures there are often environmental, not regressions.

### Toolchain

All projects target **net10.0**; `global.json` pins SDK `10.0.100` (`rollForward: latestMinor`, no prerelease) and `.github/workflows/CI.yml` installs `10.0.x` — aligned. Build with a .NET 10 SDK. CI runs `dotnet restore` → `dotnet build` → unit tests over the **whole solution** (Examples/ and Misc/ included), so verify with a full-solution build, not just the library project.

## Architecture

Fluent builder → immutable config + pluggable spider → parallel job loop.

**Build path (ADR-0025, widened by ADR-0040):** a scrape begins with a **Crawl seed**. `ScraperEngineBuilder.Crawl(urls)` / `.CrawlWithBrowser(urls)` are **static** → return `ICrawlSeed`; the seed has two strategy terminals — `.Extract(schema)` (Schema-driven JSON via the deterministic fold) or `.AsMarkdown()` (no-schema LLM-ready Markdown via `MarkdownContentExtractor`, ADR-0040) — both yielding the `ScraperEngineBuilder`. The builder's constructor is `internal` (test-only via `InternalsVisibleTo`), so it is unreachable without the seed — "build with no start URLs or no extraction strategy" is structurally impossible, not a runtime throw. It composes two internal builders:
- `ConfigBuilder` (internal) → immutable `ScraperConfig` (start URLs, the `LinkPathSelector` chain, crawl limit, headless flag).
- `SpiderBuilder` (internal) → runtime components (loaders, parsers, sinks, trackers, cookie storage), all with in-memory defaults.

Terminate with `BuildAsync()` (persists the config to `IScraperConfigStorage`, constructs a `Spider`, returns a `ScraperEngine`) or `Build()` (just the `ScraperConfig`, for a distributed start endpoint to persist). The ADR-0009 distributed-worker reduced shell is a **separate public seam**: `DistributedSpiderBuilder` (seedless, no `BuildAsync`; `.BuildSpider()` → `ISpider`) — "two seams, not one bug".

**Run path (ADR-0022):** `ScraperEngine` *is* the in-process **Crawl driver**. `RunAsync` seeds the `IScheduler` with one `Job` per start URL, then drives `Parallel.ForEachAsync` over `Scheduler.GetAllAsync()` (an async stream). Each job → `Spider.CrawlAsync` (wrapped in the **Retry policy** — `IRetryPolicy`, ADR-0026; the default `FixedAttemptsRetryPolicy` runs four attempts and never retries `OperationCanceledException`) returns a closed `JobReport`; the driver interprets it — applies the visited-link idempotency authority (atomic test-and-set), runs `ParsedData` through the page-processor pipeline (`IPageProcessor`, ADR-0038) then fans the surviving record to the sinks, enqueues child jobs, drives the **Outstanding-work latch**. The **Stop rule** verdict is a *value*, never a thrown exception (`PageCrawlLimitException` was removed in 8.0.0); on a concluded crawl the driver ends its own consumption of the job stream — `IScheduler.Complete()` was removed (ADR-0037), since durable schedulers could not honour it. Authoritative: `docs/adr/0022-crawl-driver-and-outstanding-work-latch.md`, `docs/adr/0026-retry-policy-seam.md`, `docs/adr/0037-stop-ceases-consumption.md`.

**Job model:** `Job` is a record carrying the URL, an `ImmutableQueue<LinkPathSelector>`, parent backlinks, and `PageType` (Static vs Dynamic). The crawl-vs-parse decision is **not** stored on `Job` (`Job.PageCategory` was removed) — it is computed by `CrawlStep` from the selector chain (0 left ⇒ parse the target page; exactly 1 with pagination ⇒ paginate; else follow links) and returned as a closed `CrawlOutcome` sum (`Parsed | Followed | Paginated`). Chain length is the state machine: each step dequeues one selector. Authoritative: `CONTEXT.md` + `docs/adr/0001-crawl-outcome-closed-sum.md`.

**`Spider.CrawlAsync` per job (ADR-0022 — the Spider only *reports*):**
1. Load the page via the one `IPageLoader` (ADR-0004) — HTTP by default; the headless-browser transport ships in the `WebReaper.Puppeteer` satellite (ADR-0009) — per `PageType`.
2. `CrawlStep.StepAsync` returns a closed `CrawlOutcome`; `Spider` wraps it as a `JobReport` and returns — nothing else. The **Crawl driver**, not the Spider, runs a `Parsed` outcome's `ParsedData` through the page-processor pipeline (`IPageProcessor`, ADR-0038) and fans it to every `IScraperSink`, and turns `Followed`/`Paginated` into child (and pagination) jobs.
3. Content extraction is the `IContentExtractor` seam (ADR-0039) — its one core adapter is the shared `SchemaFold<TNode>` fold over an `ISchemaBackend<TNode>` (CSS/HTML, XPath, or JSON backend); an alternative extraction strategy (e.g. an LLM extractor) implements `IContentExtractor` directly. Link extraction is the concrete `LinkExtractor` function (ADR-0036 — not a seam). Authoritative: `docs/adr/0002-schema-fold-and-node-backend-seam.md`, `docs/adr/0039-content-extractor-seam.md`.

**Pluggable seams** (public interface in `*/Abstract`). As of 9.0.0 (ADR-0023, the documented-contract surface) the `*/Concrete` implementations are `internal` — select a built-in via a `ScraperEngineBuilder` method (`.WriteToConsole()`, `.WithTextFileScheduler(...)`, a satellite's `.WithRedis*()`/`.WriteToMongoDb()`/… extension), or supply your own implementation of the public interface; you no longer `new` the concrete type. The in-memory defaults the DIY-distributed pattern wires by hand stay public. The table's "impls" are the conceptual options, reached through the builder:

| Seam | Default | Other impls |
|---|---|---|
| `IScheduler` | InMemory | File, Redis, AzureServiceBus |
| `IVisitedLinkTracker` | InMemory | File, Redis |
| `IScraperConfigStorage` | InMemory | File, MongoDb, Redis |
| `ICookiesStorage` | InMemory | File, MongoDb, Redis |
| `IScraperSink` | — | Console, Csv, JsonLines, MongoDb, Redis, Cosmos |
| page loaders | Http + Puppeteer | proxy-rotating variants when `IProxyProvider` set |

**Distributed mode:** swapping scheduler + config storage + link tracker to Redis or Azure Service Bus lets multiple workers / serverless functions share crawl state. See `Examples/WebReaper.AzureFuncs` and `Examples/WebReaper.DistributedScraperWorkerService`.

**AI-native (ADR-0040..0050):** four composing pieces on top of the existing pipeline:
- **`IContentExtractor` adapters** (ADR-0039 seam): `SchemaFold` (deterministic), `MarkdownContentExtractor` (no-schema, ADR-0040), `LlmContentExtractor` (LLM via `Microsoft.Extensions.AI`, ADR-0044 — in the `WebReaper.AI` satellite). `ExtractionRouter` (ADR-0046) composes any two — `.WithFallbackExtractor` / `.WithLlmFallback`. `SelfHealingContentExtractor` (ADR-0047) wraps the deterministic primary with an `ISelectorRepairer` — `.WithSelfHealing` / `.WithLlmSelfHealing`.
- **Page cache** (`IPageCache`, ADR-0041): cache-aside on the loader; `.WithMaxAge(TimeSpan)` is the firecrawl-shaped one-liner.
- **Discovery + monitoring**: `ScraperEngineBuilder.MapAsync(url, opts?)` for URL discovery (`ISiteMapper`, ADR-0042); `.WithChangeTracking()` for the change-tracking page processor (`IChangeStore`, ADR-0048).
- **Agent surfaces**: the `WebReaper.Cli` AOT single binary (ADR-0043) — `webreaper scrape|map|init` — is the primitive; the bundled Agent Skill is the discoverable adapter (`webreaper init` writes `.claude/skills/webreaper/SKILL.md`); the `WebReaper.Mcp` satellite (ADR-0049) is the interop adapter for MCP-only clients (Cursor, Claude Desktop, Copilot Studio).
- **Source-gen**: `[ScrapeSchema]` on a `partial class` with `[ScrapeField("selector")]` on properties — the `WebReaper.Extraction.Generators` Roslyn analyzer (ADR-0045) emits a `static Schema` and a reflection-free `static Materialize(JsonObject)`.
- **Semantic actions** (ADR-0050): `PageAction.SemanticAct(intent)` is the seventh closed-sum arm — a natural-language intent ("click sign in"). The Puppeteer transport resolves it on the first dynamic page via the registered `IActionResolver` (the `WebReaper.AI` satellite ships `LlmActionResolver`; `.WithLlmActionResolver(chatClient)` is the one-liner) into a concrete arm, dispatches it, and caches the resolution per crawl by intent string. Same LLM-as-proposer / deterministic-as-decider pattern as ADR-0046/0047 applied to actions — first page pays the LLM, every subsequent same-intent page dispatches the cached arm with no LLM call. The cache lifecycle lives in core (`SemanticActCoordinator`, unit-testable without an `IPage`); the transport delegates.

Namespace mirrors folder path throughout (`WebReaper.Core.Spider.Concrete` = `WebReaper/Core/Spider/Concrete/`).

## Gotchas

- `WriteToJsonFile` defaults `dataCleanupOnStart: true` (wipes the file on start) — opposite of the other sinks, which default `false`.
- The "JSON" sink writes **JSON Lines** (`JsonLinesFileSink`), one object per line, not a JSON array.
- First dynamic-page run downloads Chromium via Puppeteer.
- The `MarkdownContentExtractor` (ADR-0040) silently ignores the `Schema` parameter; the deterministic `SchemaFold` throws on a null Schema. Strategy-local schema requirement — the `IContentExtractor.ExtractAsync` doc explains the distinction.
- The `IPageCache` in-memory adapter (ADR-0041) is per-process; distributed crawls need a satellite adapter (future ADR). `WithMaxAge(TimeSpan.Zero)` stores but never serves — "force-fresh".
- The `[ScrapeSchema]` source generator (ADR-0045) requires the class to be `partial`; properties must have a public setter. v1 does NOT support nested `[ScrapeSchema]` types (single-level + lists of primitives only).
- The self-heal cache (ADR-0047) is keyed by Schema reference identity; the same Schema instance reused across Crawls shares its patch.
- The `WebReaper.AI` satellite pulls `Microsoft.Extensions.AI.Abstractions` (preview); not AOT-required by design (ADR-0009 quarantine).
- The `WebReaper.Mcp` satellite uses the preview `ModelContextProtocol` SDK; pin the version explicitly when shipping a release.
- `PageAction.SemanticAct(intent)` (ADR-0050) needs an `IActionResolver` — without one the transport throws `SemanticActResolutionException` on the first dispatch. `ScraperEngineBuilder.BuildAsync()` logs a warning at build time when the config carries a `SemanticAct` and the resolver is still the default `NullActionResolver`. The cache is per-Spider in-memory, intent-string-keyed (single-host crawls assumed); a multi-host crawl with intent collisions surfaces as a cached-arm dispatch failure → re-resolve loop.
- ADR-0050 widened the `WithLoadTransport(...)` factory delegate to four arguments (cookies, proxy, logger, **actionResolver**). The Puppeteer satellite's `WithPuppeteerPageLoader()` extension was updated in lockstep. A custom transport satellite needs to accept the resolver argument even if it ignores it.
