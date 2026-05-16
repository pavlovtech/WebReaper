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

### .NET version mismatch (gotcha)

All projects target **net8.0**; `global.json` requires SDK ≥ 7.0.0 (`rollForward: latestMajor`, prerelease allowed). But `.github/workflows/CI.yml` installs **6.0.x** — stale and inconsistent with what the projects actually need. Build locally with an SDK ≥ 8.

## Architecture

Fluent builder → immutable config + pluggable spider → parallel job loop.

**Build path:** `ScraperEngineBuilder` is a façade over two builders:
- `ConfigBuilder` → produces immutable `ScraperConfig` (start URLs, the `LinkPathSelector` chain, crawl limit, headless flag).
- `SpiderBuilder` → wires the runtime components (loaders, parsers, sinks, trackers, cookie storage), all with in-memory defaults.

`BuildAsync()` builds the config, persists it to `IScraperConfigStorage`, constructs a `Spider`, and returns a `ScraperEngine`.

**Run path:** `ScraperEngine.RunAsync` seeds the `IScheduler` with one `Job` per start URL, then drives `Parallel.ForEachAsync` over `Scheduler.GetAllAsync()` (an async stream). Each job → `Spider.CrawlAsync` (wrapped in `Infra.Executor.RetryAsync`, Polly-backed); resulting child jobs are pushed back into the scheduler. The loop terminates by throwing `PageCrawlLimitException` once the visited-link count hits the limit.

**Job model:** `Job` is a record carrying the URL, an `ImmutableQueue<LinkPathSelector>`, parent backlinks, and `PageType` (Static vs Dynamic). `Job.PageCategory` is **derived**, not stored — from the selector queue: 0 selectors left ⇒ `TargetPage` (parse it), 1 with pagination ⇒ `PageWithPagination`, otherwise `TransitPage` (just follow links). This is the core state machine: each crawl dequeues one selector, so the queue length drives crawl-vs-parse behavior.

**Spider.CrawlAsync per job:**
1. Load page — `IStaticPageLoader` (HTTP) or `IBrowserPageLoader` (Puppeteer) depending on `PageType`.
2. If `TargetPage`: `IContentParser` (AngleSharp) applies the parsing `Schema` → `ParsedData` → fanned out to every `IScraperSink`, plus the `PostProcessor` callback and `ScrapedData` event. No child jobs.
3. Else: `ILinkParser` extracts links for the current selector → child jobs; pagination selectors produce additional jobs.

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
- `WebReaper.csproj` `RepositoryUrl`/`Product` still say "ExoScraper" (old name); the shipped package id is `WebReaper`.
