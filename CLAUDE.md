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

**Build path:** `ScraperEngineBuilder` is a façade over two builders:
- `ConfigBuilder` → produces immutable `ScraperConfig` (start URLs, the `LinkPathSelector` chain, crawl limit, headless flag).
- `SpiderBuilder` → wires the runtime components (loaders, parsers, sinks, trackers, cookie storage), all with in-memory defaults.

`BuildAsync()` builds the config, persists it to `IScraperConfigStorage`, constructs a `Spider`, and returns a `ScraperEngine`.

**Run path:** `ScraperEngine.RunAsync` seeds the `IScheduler` with one `Job` per start URL, then drives `Parallel.ForEachAsync` over `Scheduler.GetAllAsync()` (an async stream). Each job → `Spider.CrawlAsync` (wrapped in `Infra.Executor.RetryAsync`, Polly-backed); resulting child jobs are pushed back into the scheduler. The loop terminates by throwing `PageCrawlLimitException` once the visited-link count hits the limit.

**Job model:** `Job` is a record carrying the URL, an `ImmutableQueue<LinkPathSelector>`, parent backlinks, and `PageType` (Static vs Dynamic). The crawl-vs-parse decision is **not** stored on `Job` (`Job.PageCategory` was removed) — it is computed by `CrawlStep` from the selector chain (0 left ⇒ parse the target page; exactly 1 with pagination ⇒ paginate; else follow links) and returned as a closed `CrawlOutcome` sum (`Parsed | Followed | Paginated`). Chain length is the state machine: each step dequeues one selector. Authoritative: `CONTEXT.md` + `docs/adr/0001-crawl-outcome-closed-sum.md`.

**Spider.CrawlAsync per job:**
1. Load page — `IStaticPageLoader` (HTTP) or `IBrowserPageLoader` (Puppeteer) depending on `PageType`.
2. `CrawlStep.StepAsync` returns a `CrawlOutcome`. `Parsed` ⇒ `ParsedData` fanned out to every `IScraperSink` plus the `PostProcessor` callback and `ScrapedData` event, no child jobs. `Followed`/`Paginated` ⇒ child jobs (and pagination jobs).
3. Content parsing is the shared `SchemaContentParser<TNode>` fold over an `ISchemaBackend<TNode>` (AngleSharp HTML or JSON); link extraction is `ILinkParser`. Authoritative: `docs/adr/0002-schema-fold-and-node-backend-seam.md`.

**Pluggable seams** (interface in `*/Abstract`, implementations in `*/Concrete`; swap via `ScraperEngineBuilder` methods):

| Seam | Default | Other impls |
|---|---|---|
| `IScheduler` | InMemory | File, Redis, AzureServiceBus |
| `IVisitedLinkTracker` | InMemory | File, Redis |
| `IScraperConfigStorage` | InMemory | File, MongoDb, Redis |
| `ICookiesStorage` | InMemory | File, MongoDb, Redis |
| `IScraperSink` | — | Console, Csv, JsonLines, MongoDb, Redis, Cosmos |
| page loaders | Http + Puppeteer | proxy-rotating variants when `IProxyProvider` set |

**Distributed mode:** swapping scheduler + config storage + link tracker to Redis or Azure Service Bus lets multiple workers / serverless functions share crawl state. See `Examples/WebReaper.AzureFuncs` and `Examples/WebReaper.DistributedScraperWorkerService`.

Namespace mirrors folder path throughout (`WebReaper.Core.Spider.Concrete` = `WebReaper/Core/Spider/Concrete/`).

## Gotchas

- `WriteToJsonFile` defaults `dataCleanupOnStart: true` (wipes the file on start) — opposite of the other sinks, which default `false`.
- The "JSON" sink writes **JSON Lines** (`JsonLinesFileSink`), one object per line, not a JSON array.
- `ConfigBuilder.Build()` throws if `Get`/`GetWithBrowser` or `Parse` was never called — builder order matters.
- First dynamic-page run downloads Chromium via Puppeteer.
