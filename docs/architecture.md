# WebReaper architecture and interfaces

Deep reference for the library's seam-by-seam shape. The [README](../README.md) covers the launch surface (CLI, AI features, packages, comparison); this file covers the things a .NET dev customising or extending WebReaper needs.

For the design decisions behind each seam, see the [ADRs in `docs/adr/`](adr/). The most-cited ones from this file:

- [ADR-0004: One Page Loader, two transports](adr/0004-one-page-loader-transport-seam.md)
- [ADR-0009: Registration seam + satellite adapters](adr/0009-registration-seam-and-satellite-adapters.md)
- [ADR-0022: Crawl driver and Outstanding-work latch](adr/0022-crawl-driver-and-outstanding-work-latch.md)
- [ADR-0038: Page processor seam](adr/0038-page-processor-seam.md)
- [ADR-0039: Content extractor seam](adr/0039-content-extractor-seam.md)
- [ADR-0051: Agent Crawl driver](adr/0051-agent-crawl-driver.md)

## Adding a custom sink

Out of the box the core package sends parsed data to the Console, CSV, and JSON-Lines sinks; MongoDB, Redis, and Cosmos DB sinks come from satellites. Add your own by implementing `IScraperSink`:

```csharp
using WebReaper.Sinks.Abstract;
using WebReaper.Sinks.Models;

public interface IScraperSink
{
    bool DataCleanupOnStart { get; set; }
    Task EmitAsync(ParsedData entity, CancellationToken cancellationToken = default);
}
```

`ParsedData` is `record ParsedData(string Url, JsonObject Data)`; `Data` is a `System.Text.Json.Nodes.JsonObject` (no Newtonsoft).

```csharp
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

Register with `AddSink`:

```csharp
using WebReaper.Builders;

await ScraperEngineBuilder
    .Crawl("https://example.com")
    .Extract(new() { new("name", "h1") })
    .AddSink(new ConsoleSink())
    .BuildAsync();
```

For result callbacks without a custom sink, use `.Subscribe(Action<ParsedData>)` or `.PostProcess(Func<Metadata, JsonObject, Task>)`.

## Interfaces

The library is a fluent builder over the following public seams. Implement one to plug in your own backend; the builder's registration methods compose them.

| Interface | Description |
|---|---|
| `IScheduler` | Reads and writes the job queue. Default in-memory; file, Redis, and Azure Service Bus implementations are available. |
| `IVisitedLinkTracker` | Tracks visited links. Default in-memory; file and Redis implementations are available. |
| `IPageLoader` | Turns a `PageRequest` into a page's HTML, dispatching on `PageType` to one load transport. The Spider holds one and is loader-blind. |
| `IPageLoadTransport` | The per-mechanism adapter behind `IPageLoader`: HTTP (core), Playwright (`WebReaper.Playwright`), CDP (`WebReaper.Cdp`). The only home for that mechanism's client and launch quirks and proxy application. |
| `IContentExtractor` | The content-extraction seam: takes a loaded document plus `Schema`, returns its `System.Text.Json.Nodes.JsonObject` representation. The core adapter is the deterministic `SchemaFold<TNode>` over an `ISchemaBackend`; the `WebReaper.AI` satellite ships `LlmContentExtractor`. |
| `ISchemaBackend<TNode>` | The per-document-shape seam the shared fold calls: parse a root, select many / one by selector, extract a leaf's raw value. CSS, XPath, and JSON backends ship. |
| `IScraperSink` | A destination for scraping results. Receives `ParsedData` (`Url` plus `JsonObject`). |
| `ICrawlStep` | The crawl-step decision: maps a `Job` plus loaded page plus `Schema` to a `CrawlOutcome` (parse the page, follow links, or paginate). Swap to customise crawl-vs-parse behaviour. |
| `ISpider` | The per-Job I/O shell around `ICrawlStep`: loads one page, runs the Crawl step, returns a `JobReport`, nothing else. The Crawl driver (in-process `ScraperEngine` or distributed worker) owns the visited-link tracker, the crawl-limit stop, sink fan-out, and the callbacks. Obtained from `DistributedSpiderBuilder.BuildSpider()` (ADR-0009 reduced shell). |
| `IOutstandingWorkLatch` | The Crawl driver's termination detector (ADR-0022): a unit-credit counter that trips exactly once when all work is drained. In-memory `Interlocked` adapter (in-process) and a distributed-atomic Redis adapter (`WebReaper.Redis`). |
| `IAgentBrain` | The agent driver's decision-maker (ADR-0051): picks one `AgentDecision` (`Extract`, `Follow`, `Act`, `Stop`) from the bounded `AgentState`. Default `NullAgentBrain` is a sentinel; `LlmAgentBrain` ships in `WebReaper.AI`. |
| `IAgentRunStore` | Durable agent-run state (ADR-0051). InMemory default plus File adapter in core; Redis, Mongo, SQLite, Cosmos satellite adapters. Persist-before-execute semantics. |
| `IActionResolver` | Resolves `PageAction.SemanticAct(intent)` to a concrete arm (ADR-0050). `NullActionResolver` is the default sentinel; `LlmActionResolver` ships in `WebReaper.AI`. |
| `ISelectorRepairer` | The self-healing seam (ADR-0047): proposes a repaired CSS selector when a deterministic field returns empty. |
| `ISchemaInferrer` | The schema-synthesis seam (ADR-0067): proposes a `Schema` from a URL plus optional hint. `LlmSchemaInferrer` ships in `WebReaper.AI`. |
| `ISchemaValidator` | The validator behind self-healing and re-inference (ADR-0062). Default policy preserves ADR-0029 semantics; swap via `.WithSchemaValidator(...)`. |
| `IPageProcessor` | The page-processor pipeline seam (ADR-0038). Change-tracking (ADR-0048), validation, and custom transforms register here. |
| `IRetryPolicy` | The Spider's retry seam (ADR-0026). Default `FixedAttemptsRetryPolicy` runs four attempts and never retries `OperationCanceledException`. |

## Main entities

- **Job.** A record representing one unit of work for the spider: the URL, an `ImmutableQueue<LinkPathSelector>`, parent backlinks, and `PageType` (Static vs Dynamic). Immutable; the crawl-vs-parse decision is computed by `CrawlStep`, not stored on the job (ADR-0001).
- **LinkPathSelector.** A selector for links to be crawled. The chain length is the state machine: each step dequeues one selector.
- **CrawlOutcome.** The closed sum result of a crawl step: `Parsed | Followed | Paginated` (ADR-0001). Closed at the type level; a new arm requires a deliberate language-level addition.
- **JobReport.** The Spider's report back to the Crawl driver: load result plus `CrawlOutcome`. The driver, not the Spider, fans out to sinks and enqueues child jobs (ADR-0022).
- **Schema fold.** The single recursive `Schema` interpreter (`SchemaFold<TNode>`); every backend reuses it instead of re-implementing the walk (ADR-0002).
- **AgentDecision.** The closed sum of agent actions: `Extract | Follow | Act | Stop` (ADR-0051). Each arm carries the typed fields needed to dispatch.
- **AgentDecisionOutcome.** The closed sum of execution results fed back to the brain on the next step: `Extracted | Followed | ActDispatched | Failed | Stopped | None` (ADR-0061). The brain pattern-matches on the previous outcome when deciding the next action.
- **PageAction.** An `abstract record` with seven nested sealed-record arms (`Click`, `WaitForSelector`, `ScrollToEnd`, `WaitForNetworkIdle`, `EvaluateExpression`, `Repeat`, `SemanticAct`). Closed sum; the transport dispatches with a `switch` (ADR-0035, ADR-0050).
- **RunReport.** The result of `ScraperEngine.RunAsync()` (ADR-0066): per-run stats including LLM telemetry (`Llm` field; cast to `WebReaper.AI.Llm.LlmTelemetrySnapshot` when the AI satellite is in use).

## Builder construction shape

The library has two parallel builders, mirroring the two driver shapes:

- **`ScraperEngineBuilder`** (in-process Crawl driver). Static `Crawl(urls)` / `CrawlWithBrowser(urls)` returns an `ICrawlSeed`; the seed has three strategy terminals (`Extract(schema)` / `AsMarkdown()` / `ExtractInferred(goal?)`). `BuildAsync()` returns a `ScraperEngine` ready to run.
- **`AgentEngineBuilder`** (the agent driver, ADR-0051). `Start(url, goal).WithBrain(...).BuildAsync()` returns an `AgentEngine`. Sibling to `ScraperEngineBuilder`.

A third seam, **`DistributedSpiderBuilder`** (ADR-0009), is the seedless reduced shell for distributed workers. Returns a bare `ISpider` via `BuildSpider()`; the worker's config is persisted separately by the start endpoint.

For the full design and rationale of each seam, follow the ADR links at the top of this file.
