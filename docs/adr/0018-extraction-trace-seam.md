# `IExtractionTrace` — page-lifecycle trace seam; free no-op + local-file adapters, hosted dashboard the paid future

## Status

**Proposed — design pass only** (2026-05-24). Cashes
[REPOSITIONING-PLAN §2.10](../REPOSITIONING-PLAN.md). The next plan-defined
ADR; the first one with a *paid* surface (hosted dashboard / replay UI),
all of which is gated and deferred — this ADR ships the free OSS half. No
implementation in this ADR; the proposal lives or dies on the design call.

## Context

The plan §2.10 names "DOM snapshots, HAR, trace/replay, extraction
confidence, validation diffs" as *the* number-one debugging pain in the
research digest. WebReaper today has:

- `LogToConsole()` / `ILogger` — text events, untyped.
- Sinks that capture the *result* (the JSON record).
- `Subscribe(callback)` for ad-hoc record-level visibility.

None of those answer the operator's actual question on a flaky run:

> *Why did extraction for `https://example.com/x` come back with
> `{"title": "", "summary": ""}` last Tuesday? What did the page look like
> at the time? Did the loader actually get bytes, or was it a 503? Did the
> schema match anything, or did the selector miss?*

The signal exists at four lifecycle moments — **load**, **extract**,
**process**, **emit** — and is currently uncaptured at all of them.

### What the trace is — and isn't

It IS a *page-lifecycle event stream*. One event per moment of interest,
typed. Adapters decide what to do with the events: drop them (no-op),
write them to a file (local), forward them to a hosted dashboard
(future paid satellite).

It is NOT:

- A second logger. `ILogger` events stay text-and-context; trace events
  are typed records on a closed sum.
- A second sink. Sinks consume the *extracted record*; trace consumes
  the *lifecycle* (loads, retries, fold input/output, processor
  verdicts).
- A change-tracker. ADR-0048's `ChangeTrackingProcessor` annotates the
  record; trace observes the page lifecycle around it.

### Where to instrument — the current pipeline (post-ADR-0022/0034/0038)

The plan's original pointers (`Spider.cs:99–114`) are *stale* — that
shell was the pre-ADR-0022 Spider that owned the visited-link tracker,
crawl limit, sink fan-out, and PostProcessor. ADR-0022 stripped all of
those into the **Crawl driver**; ADR-0034 stripped per-Job config-storage
reads; ADR-0038 lifted the page-processor pipeline to its own seam.
[WebReaper/Core/Spider/Concrete/Spider.cs](../../WebReaper/Core/Spider/Concrete/Spider.cs)
is now 59 lines: load → step → report.

The new instrumentation points are:

| Lifecycle moment | Where | Trace event |
|---|---|---|
| Load started | `Spider.CrawlAsync` before `PageLoader.LoadAsync` (line 51) | `PageLoadStarted(url, pageType)` |
| Load completed | `Spider.CrawlAsync` after `PageLoader.LoadAsync` | `PageLoadCompleted(url, bytes, contentType)` or `PageLoadFailed(url, exception)` |
| Extraction started | `CrawlStep` before `IContentExtractor.ExtractAsync` | `ExtractionStarted(url, schemaHash)` |
| Extraction completed | `CrawlStep` after `IContentExtractor.ExtractAsync` | `ExtractionCompleted(url, result, missingRequired)` |
| Page processed | Crawl driver after `IPageProcessor` pipeline | `PageProcessed(url, verdict)` |
| Sink emit | Crawl driver before sink fan-out | `SinkEmit(url, sinkName)` |
| Stop verdict | `ScraperEngine` when `StopRule` trips | `CrawlStopped(reason)` |

These are seven event variants on a closed-sum `TraceEvent`. The
adapters' work is "dispatch on the variant."

### Why a seam clears the ADR-0002 bar — *two real adapters, not one*

ADR-0002's discipline: "shape from the second adapter, not for it."
This seam has **two free first-party adapters** at v1:

1. **`NullExtractionTrace`** — drops every event. The default
   registration. Cost: one virtual call per event. Acceptable.
2. **`FileExtractionTrace`** — writes JSONL to a path. Solves the
   *"what happened on Tuesday"* question with a `grep`.

And one **deferred adapter** — the hosted dashboard:

3. **`WebReaper.Cloud.HostedExtractionTrace`** (future paid satellite,
   not in scope for this ADR). Forwards events to a hosted ingest
   endpoint; the dashboard renders replay (page screenshots, HAR
   playback, extraction diffs across runs).

Two real adapters is the minimum that justifies the seam. The hosted
adapter is the *commercial* third — exactly the free/paid boundary the
plan §3 names.

### Confidence + diffs — what the LocalFile adapter captures, beyond bytes

The `ExtractionCompleted` event carries the *result JsonObject* AND
`missingRequired` (the list of `Schema` leaves that came back empty).
That's the same signal `SchemaSatisfiedValidator` (ADR-0046) consumes.
The local-file adapter emits:

```json
{"ts": "...", "kind": "ExtractionCompleted", "url": "...", "result": {...}, "missingRequired": ["title", "summary"]}
```

A `grep '"missingRequired": \[[^]]' trace.jsonl` lists every page that
under-extracted, with timestamps. Cheapest possible "extraction
diagnostics."

A run-to-run diff (`extraction last Tuesday vs today`) is a deferred v2
— needs the *replay store* (paid hosted surface) to be useful at scale,
but the local-file adapter's JSONL already supports it via two-file
diff. v2 enhancement.

## Decision

Four pieces — one new seam, two free adapters, one builder hook, one
event-typed sum.

### 1. `TraceEvent` — the closed event sum

[WebReaper/Core/Observability/TraceEvent.cs](../../WebReaper/Core/Observability/TraceEvent.cs)
(new directory). Same closed-sum pattern as `PageAction` (ADR-0035) and
`CrawlOutcome` (ADR-0001):

```csharp
public abstract record TraceEvent
{
    private TraceEvent() { }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public required string Url { get; init; }

    public sealed record PageLoadStarted(PageType PageType) : TraceEvent;
    public sealed record PageLoadCompleted(int Bytes, string? ContentType) : TraceEvent;
    public sealed record PageLoadFailed(string ExceptionType, string Message) : TraceEvent;
    public sealed record ExtractionStarted(string SchemaHash) : TraceEvent;
    public sealed record ExtractionCompleted(JsonObject Result, IReadOnlyList<string> MissingRequired) : TraceEvent;
    public sealed record PageProcessed(string Verdict) : TraceEvent;
    public sealed record SinkEmit(string SinkName) : TraceEvent;
    public sealed record CrawlStopped(string Reason) : TraceEvent;
}
```

The Url + Timestamp on the base; each arm carries its own payload. No
untyped `object[]`, no kind-enum.

### 2. `IExtractionTrace` — the seam

[WebReaper/Core/Observability/Abstract/IExtractionTrace.cs](../../WebReaper/Core/Observability/Abstract/IExtractionTrace.cs).
Public, one method:

```csharp
public interface IExtractionTrace
{
    ValueTask RecordAsync(TraceEvent ev, CancellationToken cancellationToken = default);
}
```

`ValueTask` matters — the no-op adapter completes synchronously; the
hot path stays allocation-free.

### 3. `NullExtractionTrace` — the free default

[WebReaper/Core/Observability/Concrete/NullExtractionTrace.cs](../../WebReaper/Core/Observability/Concrete/NullExtractionTrace.cs).
The default registration when no adapter is supplied. Single `ValueTask`
return per call.

### 4. `FileExtractionTrace` — the free local adapter

[WebReaper/Core/Observability/Concrete/FileExtractionTrace.cs](../../WebReaper/Core/Observability/Concrete/FileExtractionTrace.cs).
JSONL writer, append-only, one event per line. Path supplied at
construction. The same buffered-drain pattern as
`JsonLinesFileSink` (ADR-0006) — write to a buffered `Channel`,
background task drains to disk. Crash-safe.

### 5. Spider + CrawlStep + Crawl driver instrumentation points

The Spider shell takes the trace at construction (one more required
input, matching ADR-0034's "run-scoped inputs at construction"):

```csharp
public Spider(
    ICrawlStep crawlStep,
    IPageLoader pageLoader,
    bool headless,
    Schema? parsingScheme,
    IExtractionTrace trace)        // new
```

`CrawlAsync` becomes:

```csharp
await trace.RecordAsync(new TraceEvent.PageLoadStarted(job.PageType) { Url = job.Url });
try {
    doc = await PageLoader.LoadAsync(...);
    await trace.RecordAsync(new TraceEvent.PageLoadCompleted(doc.Bytes, doc.ContentType) { Url = job.Url });
} catch (Exception ex) {
    await trace.RecordAsync(new TraceEvent.PageLoadFailed(ex.GetType().Name, ex.Message) { Url = job.Url });
    throw;
}

var outcome = await CrawlStep.StepAsync(job, doc, ParsingScheme, trace);  // CrawlStep takes trace too
return new JobReport(outcome, doc);
```

`CrawlStep` emits the two `Extraction*` events around its
`IContentExtractor.ExtractAsync` call. The Crawl driver
(`ScraperEngine.RunAsync`) emits `PageProcessed`, `SinkEmit`, and
`CrawlStopped`.

### 6. `ScraperEngineBuilder.WithExtractionTrace` — the builder sugar

```csharp
public ScraperEngineBuilder WithExtractionTrace(IExtractionTrace trace)
```

And a convenience:

```csharp
public ScraperEngineBuilder TraceToFile(string path)
```

Same shape as `WriteToJsonFile` — explicit one-liner for the common
case.

### Bounded scope

- **Seven event variants in v1.** The ones above. Adding new ones is
  trivially additive; the exhaustiveness analyzer fires in adapters,
  which is the desired pressure.
- **No request/response bodies in the event stream.** `PageLoadCompleted`
  carries `Bytes` (length) + `ContentType`, not the body. Bodies are
  large; storing them is the paid hosted surface's job (DOM snapshots,
  HAR). The local-file adapter ships *event-stream* JSONL, not
  *replay* tarballs. v2 enhancement adds an opt-in body-capture flag
  for the file adapter (still gated by disk-cost honesty).
- **No PII redaction in v1.** ADR-0019 (compliance, plan §2.11) owns
  PII masking. The trace adapter sees what the pipeline sees; if
  ADR-0019 lands first, PII is masked *before* the trace event is
  constructed (the mask is at the processor stage).
- **No async fan-out** — one `IExtractionTrace` per crawl. A consumer
  that wants both the file adapter and a custom adapter wraps both in
  a `CompositeExtractionTrace` (a 20-line decorator they own, not a
  core seam).
- **No sampling.** Every event fires. The hosted adapter v2 will need
  sampling for cost; the free adapters don't.

## Considered options

### (a) Reuse `ILogger` with structured events instead of a new seam — rejected

`ILogger` is untyped (parameters are `object?[]`). The trace's whole
point is the *typed event sum* — adapters dispatch on arm exhaustiveness,
not on regex over message templates. ADR-0035 chose the same way for
PageAction (rejected the kind-enum + `object[]`); the same rationale.

### (b) Single `IExtractionTrace.RecordAsync(string kind, ...)` instead of `TraceEvent` sum — rejected

The untyped-payload shape would be smaller but loses the
exhaustiveness check. The pattern across the project — `PageAction`,
`CrawlOutcome`, `PageVerdict` — is closed-sum for *every* event-like
domain. Consistency wins.

### (c) Make `IExtractionTrace` an `IPageProcessor` (reuse ADR-0038 seam) — rejected

The page-processor pipeline runs *after* extraction, in the Crawl driver
(not in the Spider). PageLoad events fire *before* extraction; they
couldn't sit in the pipeline. Extraction events fire *during* extraction;
they couldn't sit in a post-extraction stage. Two different lifecycles,
two seams.

### (d) Carry the full `doc.Content` in `PageLoadCompleted` — rejected (deferred)

Body capture is what the hosted-replay surface sells. The free
local-file adapter writes JSONL, not gigabytes of HTML. A v2 opt-in
flag (`FileExtractionTrace.CaptureBodies = true`) is the right shape
when a real caller demands it; v1 doesn't.

### (e) Trace at the `IContentExtractor` adapter layer (decorate the extractor) instead of in `CrawlStep` — rejected

A decorating extractor wouldn't see the loader events — only its own
input/output. The Spider shell already orchestrates load + step + report
(ADR-0022); the trace lives at the orchestration layer, not at one
adapter.

### (f) Make the seam *push* events via `IObservable<TraceEvent>` — rejected (deferred)

`IObservable<T>` would be Rx-style; nice for in-process subscribers but
the wrong shape for the file adapter (a sink, not an observer). A
decorator that exposes an Rx wrapper around the seam is one-page;
the seam itself stays simple.

### (g) Wait for ADR-0019 (compliance/governance) so PII redaction is in place before tracing — rejected

ADR-0019 is independently planned (§2.11). Sequencing this ADR after
0019 would defer the *number-one debugging pain* of the research digest.
The trace adapter sees what the pipeline produces; if the pipeline runs
PII redaction first (post-ADR-0019), the trace gets redacted events for
free. No ordering risk.

## Consequences

- **The plan §2.10 ships its free half.** No-op + local-file adapters
  cover dev-loop and ops debugging — the *"what happened on Tuesday"*
  question becomes a `grep` on a JSONL file.
- **The hosted dashboard surface is well-defined and gated.** The seam
  is the *exact same* contract a `WebReaper.Cloud.HostedExtractionTrace`
  satellite will implement. The free/paid boundary lands cleanly on the
  seam.
- **`IContentExtractor` is unchanged.** Tracing lives in the Spider
  shell + CrawlStep + Crawl driver, not in the extractor adapters. A
  custom `IContentExtractor` (e.g. the WebReaper.AI `LlmContentExtractor`)
  gets the trace events for free.
- **`ILogger` keeps its job.** Trace events are structured page
  lifecycle; logger remains for narrative ("starting crawl with N start
  URLs", "shutting down adapters").
- **Spider's constructor gains one parameter** — `IExtractionTrace`.
  Same pattern as ADR-0034's other run-scoped inputs. The constructor
  stays internal; the public surface (`ScraperEngineBuilder`) hides
  this — only the new `WithExtractionTrace` builder method is visible.
- **CONTEXT.md** gains an **Extraction trace** term + relationship line
  (trace → Spider's I/O wrapper → page-processor pipeline).
- **No new core deps.** `TraceEvent`, `IExtractionTrace`,
  `NullExtractionTrace`, `FileExtractionTrace` all live in
  `WebReaper.Core.Observability`. AOT-clean.

## Implementation

Proposed; no code in this ADR. The implementation slice would be:

1. **`WebReaper/Core/Observability/TraceEvent.cs`** — closed sum.
2. **`WebReaper/Core/Observability/Abstract/IExtractionTrace.cs`** —
   the seam.
3. **`WebReaper/Core/Observability/Concrete/NullExtractionTrace.cs`** —
   the no-op default.
4. **`WebReaper/Core/Observability/Concrete/FileExtractionTrace.cs`** —
   the JSONL adapter, using the ADR-0006 buffered-drain pattern.
5. **`WebReaper/Builders/SpiderBuilder.cs`** — register the trace.
6. **`WebReaper/Builders/ScraperEngineBuilder.cs`** —
   `WithExtractionTrace(IExtractionTrace)` + `TraceToFile(string path)`.
7. **`WebReaper/Core/Spider/Concrete/Spider.cs`** — new constructor
   param + the three `Load*` event emissions.
8. **`WebReaper/Core/Crawling/Concrete/CrawlStep.cs`** — the two
   `Extraction*` event emissions.
9. **`WebReaper/Core/ScraperEngine.cs`** — `PageProcessed`,
   `SinkEmit`, `CrawlStopped` emissions.
10. **`WebReaper.Tests/WebReaper.UnitTests/ExtractionTraceTests.cs`** —
    pins the seven-variant emission order, the `MissingRequired`
    capture, the null-adapter zero-allocation path.
11. **`WebReaper.Tests/WebReaper.UnitTests/FileExtractionTraceTests.cs`**
    — pins the JSONL line shape, the buffered drain, crash-safety.
12. **`docs/CONTEXT.md`** — Extraction trace term + relationship line.
13. **`CHANGELOG.md`** — under a future 10.x entry (post-10.0.0 wave).

### Guardrails (when implemented)

- `dotnet build WebReaper.sln` — 0 errors. Adapter exhaustiveness fires
  if a new arm is added without updating each adapter.
- `dotnet test WebReaper.Tests/WebReaper.UnitTests` — all pass.
- `WebReaper.AotSmokeTest` — unchanged (no AOT-touching code added to
  core).
- A new dependency-light core check entry: the `Observability/` tree
  introduces no new transitive deps.

## References

- ADR-0001 — Crawl outcome closed sum; the typed-event pattern this
  reuses.
- ADR-0002 — Schema fold + node-backend seam; the "shape from the
  second adapter" discipline. This seam clears that bar with no-op +
  local-file as the two real adapters.
- ADR-0006 — File sink buffered drain; the file adapter reuses this
  pattern.
- ADR-0019 (proposed in plan §2.11) — Compliance / governance, PII
  masking; the trace gets redacted events for free if 0019 lands first.
- ADR-0022 — Crawl driver + Outstanding-work latch; the driver hooks
  for `PageProcessed`, `SinkEmit`, `CrawlStopped`.
- ADR-0034 — Spider takes run-scoped inputs at construction; the
  shape `IExtractionTrace` lands on (one more constructor param).
- ADR-0035 — `PageAction` closed sum; the typed-event-arm pattern.
- ADR-0038 — Page-processor pipeline + Sink; `PageProcessed` and
  `SinkEmit` events bracket this stage.
- ADR-0046 — `SchemaSatisfiedValidator`; the `MissingRequired` list on
  `ExtractionCompleted` is the same validator's output.
- ADR-0048 — `ChangeTrackingProcessor`; runs in the page-processor
  pipeline; its `change_status` annotation is in the
  `PageProcessed` event's verdict payload (a future enrichment, not
  v1).
- REPOSITIONING-PLAN §2.10 — the seam this cashes.
- REPOSITIONING-PLAN §3 — the free OSS / paid hosted boundary; this is
  the first ADR with a paid future, all of it deferred.
