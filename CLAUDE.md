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

**Build path (ADR-0025):** a scrape begins with a **Crawl seed**. `ScraperEngineBuilder.Crawl(urls)` / `.CrawlWithBrowser(urls)` are **static** → return `ICrawlSeed`; `.Extract(schema)` → the `ScraperEngineBuilder`. That builder's constructor is `internal` (test-only via `InternalsVisibleTo`), so it is unreachable without the seed — "build with no start URLs or no schema" is structurally impossible, not a runtime throw. It composes two internal builders:
- `ConfigBuilder` (internal) → immutable `ScraperConfig` (start URLs, the `LinkPathSelector` chain, crawl limit, headless flag).
- `SpiderBuilder` (internal) → runtime components (loaders, parsers, sinks, trackers, cookie storage), all with in-memory defaults.

Terminate with `BuildAsync()` (persists the config to `IScraperConfigStorage`, constructs a `Spider`, returns a `ScraperEngine`) or `Build()` (just the `ScraperConfig`, for a distributed start endpoint to persist). The ADR-0009 distributed-worker reduced shell is a **separate public seam**: `DistributedSpiderBuilder` (seedless, no `BuildAsync`; `.BuildSpider()` → `ISpider`) — "two seams, not one bug".

**Run path (ADR-0022):** `ScraperEngine` *is* the in-process **Crawl driver**. `RunAsync` seeds the `IScheduler` with one `Job` per start URL, then drives `Parallel.ForEachAsync` over `Scheduler.GetAllAsync()` (an async stream). Each job → `Spider.CrawlAsync` (wrapped in the **Retry policy** — `IRetryPolicy`, ADR-0026; the default `FixedAttemptsRetryPolicy` runs four attempts and never retries `OperationCanceledException`) returns a closed `JobReport`; the driver interprets it — applies the visited-link idempotency authority (atomic test-and-set), fans `ParsedData` to the sinks, fires the `Subscribe`/`PostProcess` callbacks, enqueues child jobs, drives the **Outstanding-work latch**. Termination is a *value*, never an exception: the soft page-limit is a check the driver makes (it calls `Scheduler.Complete()`) — `PageCrawlLimitException` was removed in 8.0.0. Authoritative: `docs/adr/0022-crawl-driver-and-outstanding-work-latch.md`, `docs/adr/0026-retry-policy-seam.md`.

**Job model:** `Job` is a record carrying the URL, an `ImmutableQueue<LinkPathSelector>`, parent backlinks, and `PageType` (Static vs Dynamic). The crawl-vs-parse decision is **not** stored on `Job` (`Job.PageCategory` was removed) — it is computed by `CrawlStep` from the selector chain (0 left ⇒ parse the target page; exactly 1 with pagination ⇒ paginate; else follow links) and returned as a closed `CrawlOutcome` sum (`Parsed | Followed | Paginated`). Chain length is the state machine: each step dequeues one selector. Authoritative: `CONTEXT.md` + `docs/adr/0001-crawl-outcome-closed-sum.md`.

**`Spider.CrawlAsync` per job (ADR-0022 — the Spider only *reports*):**
1. Load the page via the one `IPageLoader` (ADR-0004) — HTTP by default; the headless-browser transport ships in the `WebReaper.Puppeteer` satellite (ADR-0009) — per `PageType`.
2. `CrawlStep.StepAsync` returns a closed `CrawlOutcome`; `Spider` wraps it as a `JobReport` and returns — nothing else. The **Crawl driver**, not the Spider, fans a `Parsed` outcome's `ParsedData` to every `IScraperSink` + the `PostProcess`/`Subscribe` callbacks, and turns `Followed`/`Paginated` into child (and pagination) jobs.
3. Content parsing is the shared `SchemaContentParser<TNode>` fold over an `ISchemaBackend<TNode>` (AngleSharp HTML or JSON); link extraction is `ILinkParser`. Authoritative: `docs/adr/0002-schema-fold-and-node-backend-seam.md`.

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

Namespace mirrors folder path throughout (`WebReaper.Core.Spider.Concrete` = `WebReaper/Core/Spider/Concrete/`).

## Gotchas

- `WriteToJsonFile` defaults `dataCleanupOnStart: true` (wipes the file on start) — opposite of the other sinks, which default `false`.
- The "JSON" sink writes **JSON Lines** (`JsonLinesFileSink`), one object per line, not a JSON array.
- First dynamic-page run downloads Chromium via Puppeteer.
