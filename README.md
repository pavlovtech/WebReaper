![logo](https://user-images.githubusercontent.com/6662454/221978697-3f35564a-f442-46e6-9182-f2604a17e1f6.png)

# WebReaper

[![NuGet](https://img.shields.io/nuget/v/WebReaper)](https://www.nuget.org/packages/WebReaper)
[![build status](https://github.com/pavlovtech/WebReaper/actions/workflows/CI.yml/badge.svg)](https://github.com/pavlovtech/WebReaper/actions/workflows/CI.yml)

## Overview

WebReaper is a declarative, high-performance web scraper, crawler and parser in C#. Crawl any web site,
parse the data, and save the structured result to a file, a database, or pretty much anywhere you want — with
a simple, extensible fluent API.

As of **9.0.0** the core `WebReaper` package is **dependency-light, Native-AOT-ready and Newtonsoft-free**:
a plain HTTP → file crawl pulls only AngleSharp, `Microsoft.Extensions.*` and Polly. Heavier capabilities
(headless browser, MongoDB, Redis, Azure Cosmos DB, Azure Service Bus, SQLite-backed local durable
scheduler/tracker) ship as **optional satellite packages** you add only when you need them — see
[Packages](#packages).

## Quick start

```
dotnet add package WebReaper
```

```C#
using WebReaper.Builders;

var engine = await ScraperEngineBuilder
    .Crawl("https://www.alexpavlov.dev/blog")
    .Extract(new()
    {
        new("title", ".text-3xl.font-bold"),
        new("text", ".max-w-max.prose.prose-dark")
    })
    .Follow("a.text-gray-900.transition")
    .WriteToJsonFile("output.json")
    .PageCrawlLimit(10)
    .WithParallelismDegree(30)
    .LogToConsole()
    .BuildAsync();

await engine.RunAsync();
```

That example is pure HTTP — no browser, no extra packages. For JavaScript-rendered pages, add
`WebReaper.Puppeteer` (see [Parsing dynamic pages](#parsing-dynamic-pages-spa)).

## Table of contents

- [Install](#install)
- [Packages](#packages)
- [Requirements](#requirements)
- [Features](#features)
- [Usage examples](#usage-examples)
- [API overview](#api-overview)
  * [Parsing dynamic pages (SPA)](#parsing-dynamic-pages-spa)
  * [Running JavaScript / page actions](#running-javascript--page-actions)
  * [Persist the progress locally](#persist-the-progress-locally)
  * [Authorization](#authorization)
  * [How to disable headless mode](#how-to-disable-headless-mode)
  * [Cleaning data from a previous run](#cleaning-data-from-a-previous-run)
  * [Distributed and serverless scraping](#distributed-and-serverless-scraping)
  * [Storage and scheduler backends](#storage-and-scheduler-backends)
  * [Extensibility: adding a sink](#extensibility-adding-a-sink)
  * [Interfaces](#interfaces)
  * [Main entities](#main-entities)
- [Repository structure](#repository-structure)
- [License](#license)

## Install

Core package — HTTP crawling/parsing, in-memory and file-backed state, Console/CSV/JSON-Lines sinks:

```
dotnet add package WebReaper
```

Add a satellite only for the capability you need (each brings its own SDK so the core stays light):

```
dotnet add package WebReaper.Puppeteer        # headless-browser (SPA / JS) pages
dotnet add package WebReaper.Mongo            # MongoDB sink + config/cookie storage
dotnet add package WebReaper.Redis            # Redis scheduler, tracker, sink, storage
dotnet add package WebReaper.AzureServiceBus  # Azure Service Bus distributed scheduler
dotnet add package WebReaper.Cosmos           # Azure Cosmos DB sink
dotnet add package WebReaper.Sqlite           # SQLite local durable scheduler + visited-link tracker
```

## Packages

All seven packages are versioned in lockstep — the latest published version is `9.0.0`; the
next wave, **`10.0.0`, is accumulating on `master` and is not yet released**. Core and the six
satellites move together in release waves (ADR-0022 → `8.0.0`, ADR-0023 → `9.0.0`, ADR-0025 →
`10.0.0` *(unreleased)*); `WebReaper.Sqlite`, added at `7.1.0`, joined the lockstep from `8.0.0`. All packages are GPL-3.0-or-later, and every satellite
wires itself in through the builder's public registration seam.

| Package | Add it for | Key builder calls |
|---|---|---|
| **WebReaper** | Core. HTTP crawl/parse, in-memory + file scheduler / visited-link tracker / cookie & config storage, Console / CSV / JSON-Lines sinks. Dependency-light, Native-AOT-ready, Newtonsoft-free. | `Crawl` `Extract` `Follow` `Paginate` `WriteToJsonFile` `WriteToCsvFile` `WriteToConsole` |
| **WebReaper.Puppeteer** | Headless-browser loading of SPA / JavaScript pages | `.WithPuppeteerPageLoader()` + `CrawlWithBrowser` / `FollowWithBrowser` / `PaginateWithBrowser` |
| **WebReaper.Mongo** | MongoDB result sink and MongoDB-backed config / cookie storage | `.WriteToMongoDb(...)` `.WithMongoDbConfigStorage(...)` `.WithMongoDbCookieStorage(...)` |
| **WebReaper.Redis** | Redis scheduler, visited-link tracker, result sink, config / cookie storage | `.WithRedisScheduler(...)` `.TrackVisitedLinksInRedis(...)` `.WriteToRedis(...)` `.WithRedisConfigStorage(...)` `.WithRedisCookieStorage(...)` |
| **WebReaper.AzureServiceBus** | Distributed scheduler over an Azure Service Bus queue | `.WithAzureServiceBusScheduler(...)` |
| **WebReaper.Cosmos** | Azure Cosmos DB result sink | `.WriteToCosmosDb(...)` |
| **WebReaper.Sqlite** | Local **durable** scheduler & visited-link tracker on an embedded SQLite store — resume is a query, no position file. Opt-in robust-local tier (no server, unlike Redis). | `.WithSqliteScheduler(...)` `.TrackVisitedLinksInSqlite(...)` |

> The core default page loader is **HTTP-only**. Crawling a dynamic page (`CrawlWithBrowser` /
> `FollowWithBrowser` / `PaginateWithBrowser`) without `WebReaper.Puppeteer` registered throws an
> `InvalidOperationException` telling you to add the package and call `.WithPuppeteerPageLoader()`.

## Requirements

.NET 10. The core package is `IsAotCompatible` — it Native-AOT-publishes with zero trim/AOT warnings
(proven by the AOT smoke test in CI). Satellites carry their own SDK dependencies and are not AOT-clean by
design; reference one only when you use it.

## Features

* :zap: High crawling speed through parallelism and asynchrony
* 🗒 Declarative and easy to use
* 🪶 Dependency-light, Native-AOT-ready, Newtonsoft-free core
* 💾 Console, CSV and JSON-Lines sinks out of the box; MongoDB, Redis and Azure Cosmos DB via satellites
* :earth_americas: Scalable: run on cloud VMs, serverless functions or on-prem; go distributed with Redis or Azure Service Bus
* :octopus: Crawl and parse Single Page Applications with Puppeteer (`WebReaper.Puppeteer`)
* 🖥 Proxy support
* 🌀 Extensible: replace any out-of-the-box seam with your own implementation

## Usage examples

* Data mining
* Gathering data for machine learning
* Online price-change monitoring and price comparison
* News aggregation
* Product-review scraping (to watch the competition)
* Tracking online presence and reputation

## API overview

### Parsing dynamic pages (SPA)

Parsing Single Page Applications is simple: use `CrawlWithBrowser` and/or `FollowWithBrowser`, add the
`WebReaper.Puppeteer` package, and register it with `.WithPuppeteerPageLoader()`. Puppeteer then loads
those pages in a headless browser.

```
dotnet add package WebReaper.Puppeteer
```

```C#
using WebReaper.Builders;
using WebReaper.Puppeteer;

var engine = await ScraperEngineBuilder
    .CrawlWithBrowser("https://www.alexpavlov.dev/blog")
    .Extract(new()
    {
        new("title", ".text-3xl.font-bold"),
        new("text", ".max-w-max.prose.prose-dark")
    })
    .WithPuppeteerPageLoader()
    .FollowWithBrowser("a.text-gray-900.transition")
    .WriteToJsonFile("output.json")
    .PageCrawlLimit(10)
    .WithParallelismDegree(30)
    .LogToConsole()
    .BuildAsync();

await engine.RunAsync();
```

`.WithPuppeteerPageLoader()` is parameterless and reproduces the pre-7.0 behaviour exactly (one shared
cookie container, optional proxy applied the browser's own way). The first dynamic-page run downloads
Chromium via Puppeteer.

### Running JavaScript / page actions

You can run JavaScript and drive the page as it loads in the headless browser. Pass an actions lambda
(e.g. `.ScrollToEnd()`) — useful when the content you need appears only after clicks, scrolls, etc.

```C#
using WebReaper.Builders;
using WebReaper.Puppeteer;

var engine = await ScraperEngineBuilder
    .CrawlWithBrowser("https://www.reddit.com/r/dotnet/", actions => actions
        .ScrollToEnd()
        .Build())
    .Extract(new()
    {
        new("title", "._eYtD2XCVieq6emjKBH3m"),
        new("text", "._3xX726aBn29LDbsDtzr_6E._1Ap4F5maDtT1E1YuCiaO0r.D3IL3FD0RFy_mkKLPwL4")
    })
    .Follow("a.SQnoC3ObvgnGjWt90zD9Z._2INHSNB8V5eaWp4P0rY_mE")
    .WithPuppeteerPageLoader()
    .WriteToJsonFile("output.json")
    .LogToConsole()
    .BuildAsync();

await engine.RunAsync();

Console.ReadLine();
```

`PageActionBuilder` exposes `Click`, `Wait`, `ScrollToEnd`, `WaitForSelector`, `WaitForNetworkIdle`,
`EvaluateExpression`, `Repeat`/`RepeatWithDelay`, and `Build()`.

### Persist the progress locally

To persist the job queue and visited links locally — so you can resume where you left off — use
`WithTextFileScheduler` and `TrackVisitedLinksInFile`:

```C#
using WebReaper.Builders;

var engine = await ScraperEngineBuilder
    .Crawl("https://rutracker.org/forum/index.php?c=33")
    .Extract(new()
    {
        new("name", "#topic-title"),
        new("category", "td.nav.t-breadcrumb-top.w100.pad_2>a:nth-child(3)"),
        new("subcategory", "td.nav.t-breadcrumb-top.w100.pad_2>a:nth-child(5)"),
        new("torrentSize", "div.attach_link.guest>ul>li:nth-child(2)"),
        new("torrentLink", ".magnet-link", "href"),
        new("coverImageUrl", ".postImg", "src")
    })
    .WithLogger(logger)
    .Follow("#cf-33 .forumlink>a")
    .Follow(".forumlink>a")
    .Paginate("a.torTopic", ".pg")
    .WriteToJsonFile("result.json")
    .IgnoreUrls(blackList)
    .WithTextFileScheduler("jobs.txt", "currentJob.txt")
    .TrackVisitedLinksInFile("links.txt")
    .BuildAsync();
```

The file scheduler is the zero-dependency default: an append-only job file, a 300 ms poll and a
sidecar position file. For a long single-machine crawl that must survive `kill -9` and resume by
query — without standing up a Redis server — add the `WebReaper.Sqlite` satellite and swap the two
local backends. "Resume" becomes a `SELECT` over an indexed table; there is no position file to keep
in sync (the visited-link table *is* the set — no in-memory mirror). The core file adapters are
unchanged and stay the default; this is opt-in:

```C#
using WebReaper.Builders;
using WebReaper.Sqlite;   // dotnet add package WebReaper.Sqlite

var engine = await ScraperEngineBuilder
    .Crawl("https://rutracker.org/forum/index.php?c=33")
    .Extract(new() { new("name", "#topic-title") })
    .Follow(".forumlink>a")
    .Paginate("a.torTopic", ".pg")
    .WriteToJsonFile("result.json")
    .WithSqliteScheduler("crawl/state.db")        // resume is a query, not a position file
    .TrackVisitedLinksInSqlite("crawl/state.db")  // the table is the set
    .BuildAsync();
```

Pass `dataCleanupOnStart: true` to either call to start a fresh crawl (clears that table at start).

### Authorization

If the site needs authorization, call `SetCookies` and fill the `CookieContainer` with the cookies
required. You perform the login yourself; WebReaper only uses the cookies you provide.

```C#
using System.Net;
using WebReaper.Builders;

var engine = await ScraperEngineBuilder
    .Crawl("https://rutracker.org/forum/index.php?c=33")
    .Extract(new() { new("name", "#topic-title") })
    .WithLogger(logger)
    .SetCookies(cookies =>
    {
        cookies.Add(new Cookie("AuthToken", "123"));
    })
    // ...
    .BuildAsync();
```

### How to disable headless mode

When scraping with a browser (`CrawlWithBrowser` / `FollowWithBrowser`, via `WebReaper.Puppeteer`) the
default is headless — you don't see the browser. Seeing it can help with debugging; disable headless mode
with `.HeadlessMode(false)`:

```C#
using WebReaper.Builders;
using WebReaper.Puppeteer;

var engine = await ScraperEngineBuilder
    .CrawlWithBrowser("https://www.reddit.com/r/dotnet/", actions => actions
        .ScrollToEnd()
        .Build())
    .Extract(new() { new("title", "._eYtD2XCVieq6emjKBH3m") })
    .HeadlessMode(false)
    .WithPuppeteerPageLoader()
    // ...
    .BuildAsync();
```

### Cleaning data from a previous run

To start fresh, pass `dataCleanupOnStart: true` to the relevant builder method.

```C#
// Result file — note: WriteToJsonFile already defaults dataCleanupOnStart to TRUE
.WriteToJsonFile("output.json", dataCleanupOnStart: true)

// Visited-link tracker
.TrackVisitedLinksInFile("visited.txt", dataCleanupOnStart: true)

// Job queue / scheduler
.WithTextFileScheduler("jobs.txt", "currentJob.txt", dataCleanupOnStart: true)
```

The `dataCleanupOnStart` parameter exists on the satellite sinks too (e.g. `WriteToMongoDb`,
`WriteToRedis`, `WriteToCosmosDb`). Note `WriteToJsonFile` defaults it to `true` (it wipes the file on
start) — the opposite of the other sinks, which default to `false`. The "JSON" file sink writes
**JSON Lines** (one compact JSON object per line), not a JSON array.

### Distributed and serverless scraping

Swap the scheduler, config storage and link tracker to Redis or Azure Service Bus and multiple
workers / serverless functions can share one crawl. `Examples/WebReaper.AzureFuncs` shows the serverless
shape with two functions:

* **StartScraping** builds the scraper configuration, seeds the distributed Outstanding-work latch,
  and enqueues the first job (the start URL) onto the queue (e.g. Azure Service Bus).
* **WebReaperSpider** is the distributed **Crawl driver**, triggered by each queued job. It gets a
  bare `ISpider` from `new DistributedSpiderBuilder()...BuildSpider()` (load → Crawl step →
  `JobReport`), then interprets the report: an atomic visited-link test-and-set gates
  duplicates/redeliveries, a parsed page is fanned out to the sink, discovered child jobs are
  enqueued back onto the queue, and a distributed Outstanding-work latch detects when all work has
  drained. It never throws to signal the crawl limit, so the queue is never poisoned (ADR-0022).

`DistributedSpiderBuilder.BuildSpider()` (the ADR-0009 distributed-worker seam) returns an `ISpider`
without building or persisting a `ScraperConfig`; it has no Crawl seed and no `BuildAsync` — the
worker's config is persisted separately by the start endpoint
(`ScraperEngineBuilder.Crawl(...).Extract(...).Build()`) and read from storage at crawl time. This is
the "two seams, not one bug" split (ADR-0025). See also
`Examples/WebReaper.DistributedScraperWorkerService`.

### Storage and scheduler backends

Every backend is a swappable seam. In-memory is the default; file-backed lives in core; the rest come
from satellites.

| Seam | Core (in-memory default + file) | Satellite options |
|---|---|---|
| Scheduler | in-memory, `WithTextFileScheduler` | `WithSqliteScheduler` (SQLite, local durable), `WithRedisScheduler` (Redis), `WithAzureServiceBusScheduler` (Azure Service Bus) |
| Visited-link tracker | in-memory, `TrackVisitedLinksInFile` | `TrackVisitedLinksInSqlite` (SQLite, local durable), `TrackVisitedLinksInRedis` (Redis) |
| Config storage | in-memory, `WithFileConfigStorage` | `WithMongoDbConfigStorage`, `WithRedisConfigStorage` |
| Cookie storage | in-memory, `WithFileCookieStorage` | `WithMongoDbCookieStorage`, `WithRedisCookieStorage` |
| Result sink | `WriteToConsole`, `WriteToCsvFile`, `WriteToJsonFile` | `WriteToMongoDb`, `WriteToRedis`, `WriteToCosmosDb` |
| Page loader | HTTP (default) | `WithPuppeteerPageLoader()` (headless browser) |

### Extensibility: adding a sink

Out of the box the core package sends parsed data to the Console, CSV and JSON-Lines sinks; MongoDB,
Redis and Cosmos DB sinks come from satellites. Add your own by implementing `IScraperSink`:

```C#
using WebReaper.Sinks.Abstract;
using WebReaper.Sinks.Models;

public interface IScraperSink
{
    bool DataCleanupOnStart { get; set; }
    Task EmitAsync(ParsedData entity, CancellationToken cancellationToken = default);
}
```

`ParsedData` is `record ParsedData(string Url, JsonObject Data)` — `Data` is a
`System.Text.Json.Nodes.JsonObject` (no Newtonsoft). A minimal console sink:

```C#
using System.Text.Json.Nodes;
using WebReaper.Sinks.Abstract;
using WebReaper.Sinks.Models;

public class ConsoleSink : IScraperSink
{
    public bool DataCleanupOnStart { get; set; }

    public Task EmitAsync(ParsedData entity, CancellationToken cancellationToken = default)
    {
        Console.WriteLine(entity.Data.ToJsonString());
        return Task.CompletedTask;
    }
}
```

Register it with `AddSink`:

```C#
using WebReaper.Builders;

var engine = await ScraperEngineBuilder
    .Crawl("https://rutracker.org/forum/index.php?c=33")
    .Extract(new()
    {
        new("name", "#topic-title"),
    })
    .AddSink(new ConsoleSink())
    .Follow("#cf-33 .forumlink>a")
    .Follow(".forumlink>a")
    .Paginate("a.torTopic", ".pg")
    .BuildAsync();
```

For result callbacks without a custom sink, use `.Subscribe(Action<ParsedData>)` or
`.PostProcess(Func<Metadata, JsonObject, Task>)`.

### Interfaces

| Interface | Description |
|---|---|
| `IScheduler` | Reads and writes the job queue. Default is in-memory; file, Redis and Azure Service Bus implementations are available. |
| `IVisitedLinkTracker` | Tracks visited links. Default is in-memory; file and Redis implementations are available. |
| `IPageLoader` | Turns a `PageRequest` into a page's HTML, dispatching on `PageType` to one load transport. The Spider holds one and is loader-blind. |
| `IPageLoadTransport` | The per-mechanism adapter behind `IPageLoader`: HTTP (core) or headless browser (`WebReaper.Puppeteer`). The only home for that mechanism's client/launch quirks and proxy application. |
| `IContentExtractor` | The content-extraction seam: takes a loaded document + `Schema`, returns its `System.Text.Json.Nodes.JsonObject` representation. The core adapter is the deterministic `SchemaFold<TNode>` over an `ISchemaBackend` (`WithXPathContentParser()` / `WithJsonContentParser()` select the XPath / JSON backend). Implement it directly for an alternative extraction strategy, e.g. an LLM-backed extractor. |
| `ISchemaBackend<TNode>` | The per-document-shape seam the shared fold calls: parse a root, select many / one by selector, extract a leaf's raw value. The shipped CSS, XPath and JSON backends implement this. |
| `IScraperSink` | A destination for scraping results. Receives `ParsedData` (`Url` + `JsonObject`). |
| `ICrawlStep` | The crawl-step decision: maps a `Job` + loaded page + `Schema` to a `CrawlOutcome` (parse the page, follow links, or paginate). Swap it to customize crawl-vs-parse behavior. |
| `ISpider` | The per-Job I/O shell around `ICrawlStep`: loads one page, runs the Crawl step, and returns a `JobReport` — nothing else. The Crawl driver (in-process `ScraperEngine` or the distributed worker) owns the visited-link tracker, the crawl-limit stop, sink fan-out and the callbacks. Obtained from `DistributedSpiderBuilder.BuildSpider()` (the ADR-0009 reduced shell). |
| `IOutstandingWorkLatch` | The Crawl driver's termination detector (ADR-0022): a unit-credit counter that trips exactly once when all work is drained. In-memory `Interlocked` adapter (in-process) and a distributed-atomic Redis adapter (`WebReaper.Redis`). |

### Main entities

* **Job** — a record representing one unit of work for the spider.
* **LinkPathSelector** — a selector for links to be crawled.
* **CrawlOutcome** — the closed result of a crawl step: a parsed target page, followed links, or paginated pages.
* **Schema fold** — the single recursive `Schema` interpreter (`SchemaFold<TNode>`); every backend reuses it instead of re-implementing the walk.

## Repository structure

| Project | Description |
|---|---|
| `WebReaper` | The core library (the `WebReaper` NuGet package). |
| `WebReaper.Puppeteer` | Satellite: headless-browser page loader. |
| `WebReaper.Mongo` | Satellite: MongoDB sink + config/cookie storage. |
| `WebReaper.Redis` | Satellite: Redis scheduler, tracker, sink, config/cookie storage. |
| `WebReaper.AzureServiceBus` | Satellite: Azure Service Bus distributed scheduler. |
| `WebReaper.Cosmos` | Satellite: Azure Cosmos DB sink. |
| `Examples/WebReaper.ConsoleApplication` | Using WebReaper in a console application. |
| `Examples/WebReaper.ScraperWorkerService` | Using WebReaper in a .NET Worker Service. |
| `Examples/WebReaper.DistributedScraperWorkerService` | Distributed crawl across workers sharing crawl state. |
| `Examples/WebReaper.AzureFuncs` | Serverless crawl with Azure Functions + Azure Service Bus. |
| `Examples/BrownsfashionScraper` | A real-world e-commerce scraper example. |
| `Misc/WebReaper.ProxyProviders` | Example proxy-provider implementations. |

## License

See the [LICENSE](LICENSE.txt) file for license rights and limitations (GNU GPLv3).
