# Engine cost telemetry — `RunReport` returned by `ScraperEngine.RunAsync` / `AgentEngine.RunAsync`

## Status

**Accepted — design pass** (2026-05-25). Second ADR of the v10.0.0
pre-tag cost-optimisation slice. Pairs with **ADR-0065** which adds
the per-call `InputTokens` / `OutputTokens` / `CachedInputTokens` /
`TotalTokens` fields this ADR aggregates into a per-run summary. Also
closes the long-standing `AgentEngineOptions.MaxBudgetTokens` gap —
the field has been documented since ADR-0051 but never actually
enforced (grep confirmed). Folds into v10.0.0 — the tag waits on this
PR.

## Context

ADR-0059 centralised the four LLM adapters' mechanism in
`LlmCall<TResponse>`; ADR-0065 expanded `LlmCallResult` with the
input/output/cached split. The data is now produced — but no
engine-level surface aggregates it. Consumers asking "what did this
crawl cost?" / "what did this agent run spend?" / "which adapter
burned the most tokens?" have no answer.

Three concrete gaps:

1. **`ScraperEngine.RunAsync` returns plain `Task`.** No surface for
   any per-run summary. A consumer who wants to log "scrape complete:
   42 pages, 87 k input tokens, $0.21 estimated" writes their own
   bookkeeping by attaching listeners — and the engine doesn't expose
   one.
2. **`AgentEngine.RunAsync` returns `AgentResult`** (6 fields:
   `RunId`, `Records`, `TerminationReason`, `History`, `VisitedUrls`,
   `StepsExecuted`) — no token / cost field.
3. **`AgentEngineOptions.MaxBudgetTokens` is silently inert.** The
   XML doc on the option says *"the engine reads
   `ChatResponse.Usage.TotalTokenCount` from whichever brain reports
   it"*. Grep of `AgentEngine.cs` shows zero references to
   `MaxBudgetTokens` after construction — the field is captured into
   `_options` and never read. A consumer setting `MaxBudgetTokens: 1_000_000`
   gets no protection.

The cause is structural: the engines don't hold the `IChatClient`
references — LLM calls happen deep inside the `IContentExtractor` /
`IActionResolver` / `IAgentBrain` / `ISelectorRepairer` adapters, each
of which constructs its own `LlmCall<T>`. The engine sees only the
adapter return values. Telemetry needs to flow *upward* from the
mechanism layer to the engine.

Three motivations:

- **Cost visibility.** Consumers can't optimise what they can't see.
  ADR-0065's cache-hint default saves money silently — without a
  per-run cached-vs-uncached split, no consumer can verify that the
  saving is real.
- **Per-adapter attribution.** The four adapters spend different
  budgets — the brain (~2 k system prompt + tool registry, every step)
  dominates the extractor (~1.5 k, every parsed page) which dominates
  the resolver (~0.8 k, first-of-intent only). Without
  `descriptor.Name` attribution, a consumer optimising one role
  can't tell if they targeted the right one.
- **MaxBudgetTokens has to actually work.** A silently-inert safety
  cap is worse than no cap — the consumer thinks they're protected.

### What this ADR does and does not move

**Moves (satellite + core):**
- New `ILlmCallTelemetry` seam in `WebReaper.AI/Llm/` — single-method
  interface; `NullLlmCallTelemetry` default.
- `LlmCallTelemetry` thread-safe accumulator (default impl) producing
  `LlmTelemetrySnapshot` records.
- `LlmCall<TResponse>` gains an optional `ILlmCallTelemetry?` ctor
  arg; each successful `InvokeAsync` reports a `LlmCallUsage` value.
- The four `Llm*` adapters gain an optional `ILlmCallTelemetry?` ctor
  arg; thread it through to `LlmCall<T>`.
- `RunReport` core record (`WebReaper/Domain/Telemetry/RunReport.cs`)
  with `LlmTelemetrySnapshot Llm` + `TimeSpan Duration`.
- `ScraperEngine.RunAsync` return type changes from `Task` →
  `Task<RunReport>` (consumer code `await engine.RunAsync(ct)` is
  forwards-compatible — discard semantics).
- `AgentResult` grows a `RunReport Report` init-only field (record
  evolution, defensive default for back-compat with anyone
  deconstructing).
- `AgentEngine.RunAsync` enforces `MaxBudgetTokens` against
  `_telemetry.Snapshot().TotalTokens` between steps, matching ADR-0051's
  fork-6 termination precedence (brain `Stop` → `MaxSteps` →
  `MaxBudgetTokens` → caller cancellation).
- `ScraperEngineBuilder` + `AgentEngineBuilder` each gain an internal
  `_telemetry` field constructed lazily on first `WithLlm*` /
  `.UseAi()` registration; passed to constructed adapters and to the
  engine at `BuildAsync` time.

**Stays out of scope (v1):**
- **Cost (USD) computation.** Pricing changes too often (OpenAI /
  Anthropic update lists monthly); locking a price table into the
  library would age out fast. The report ships *tokens*; consumers
  multiply by their current per-model rate. A pluggable pricing
  seam is a v2 question.
- **Per-page telemetry attribution.** The accumulator keys by
  `descriptor.Name` (per-adapter); per-page-per-adapter is a v2
  widening (would require URL context propagation through the
  mechanism — non-trivial).
- **Streaming / mid-run inspection.** `RunReport` is returned at the
  end. A consumer wanting live updates polls
  `engine.GetSnapshot()` — *not in v1*. v2 could add it; the
  underlying `LlmCallTelemetry` is already snapshot-safe.
- **OpenTelemetry export.** M.E.AI ships `ChatClientBuilder.UseOpenTelemetry(...)`
  — orthogonal, already available to consumers who wrap their
  `IChatClient`. No reason to re-implement.
- **Scraper-side `MaxBudgetTokens`.** Only `AgentEngineOptions` has a
  budget cap today; adding one to `ScraperConfig` is a separate
  decision (the scraper's LLM use is per-page-cap-driven, not
  per-run-cap-driven). v2 question.
- **Per-run telemetry isolation across builder re-use.** A consumer
  who calls `BuildAsync` twice on the same builder shares one
  telemetry instance. Documented; `engine.RunAsync` resets the
  accumulator at the start of each run so a second run starts fresh.

## Decision

Seven pieces — three new types in the satellite, one new record in
core, one constructor arg on `LlmCall<T>` + four adapters, return
type changes on both engines, one new field on `AgentResult`. The
`IChatClient` binding stays in the satellite (ADR-0009 quarantine);
the `RunReport` shell type lives in core so it can be returned by the
core engines without core taking an AI dep.

### 1. `LlmCallUsage` — per-call telemetry value

`WebReaper.AI/Llm/LlmCallUsage.cs`. Public record:

```csharp
namespace WebReaper.AI.Llm;

/// <summary>
/// One LLM call's usage — produced by <see cref="LlmCall{TResponse}"/>'s
/// <see cref="LlmCall{TResponse}.InvokeAsync"/> and reported to the
/// registered <see cref="ILlmCallTelemetry"/>. Carried as a record so
/// per-adapter accumulators can pattern-match on the descriptor name
/// for breakdown reporting.
/// </summary>
public sealed record LlmCallUsage(
    string DescriptorName,
    long? InputTokens,
    long? OutputTokens,
    long? CachedInputTokens,
    long? TotalTokens,
    int ParseRetries,
    TimeSpan Duration);
```

Mirrors the fields ADR-0065 added to `LlmCallResult` plus
`DescriptorName` (the per-adapter attribution key) and `Duration`
(stopwatch around the chat client call — cheap, useful for latency
breakdown). Doesn't carry the parsed value or raw response — those
are call-result concerns, not telemetry.

### 2. `ILlmCallTelemetry` seam + `LlmCallTelemetry` default

`WebReaper.AI/Llm/ILlmCallTelemetry.cs`:

```csharp
namespace WebReaper.AI.Llm;

/// <summary>
/// The accumulator <see cref="LlmCall{TResponse}"/> reports completed
/// calls into. One instance per engine run — the engine owns it,
/// adapters thread it through their <see cref="LlmCall{TResponse}"/>
/// constructions. Default <see cref="NullLlmCallTelemetry"/> is the
/// no-op for à-la-carte adapter construction outside an engine.
/// </summary>
public interface ILlmCallTelemetry
{
    /// <summary>Record one completed call. Thread-safe (called from
    /// parallel <c>ScraperEngine</c> workers).</summary>
    void Record(LlmCallUsage usage);

    /// <summary>Read the current aggregate. Thread-safe; returns an
    /// immutable point-in-time copy.</summary>
    LlmTelemetrySnapshot Snapshot();

    /// <summary>Clear the accumulator. Called by the engine at the
    /// start of <c>RunAsync</c> to isolate each run.</summary>
    void Reset();
}

public sealed class NullLlmCallTelemetry : ILlmCallTelemetry
{
    public static readonly NullLlmCallTelemetry Instance = new();
    private NullLlmCallTelemetry() { }
    public void Record(LlmCallUsage usage) { /* discard */ }
    public LlmTelemetrySnapshot Snapshot() => LlmTelemetrySnapshot.Empty;
    public void Reset() { /* no-op */ }
}
```

Default `LlmCallTelemetry` impl in `WebReaper.AI/Llm/LlmCallTelemetry.cs`:

```csharp
public sealed class LlmCallTelemetry : ILlmCallTelemetry
{
    private readonly ConcurrentDictionary<string, AdapterStats> _byAdapter = new();
    private long _totalCalls;
    private long _totalInputTokens;
    private long _totalOutputTokens;
    private long _totalCachedInputTokens;
    private long _totalTokens;
    private long _totalDurationTicks;
    private long _totalParseRetries;

    public void Record(LlmCallUsage usage) { /* Interlocked.Add on each total + AddOrUpdate per-adapter */ }
    public LlmTelemetrySnapshot Snapshot() { /* construct immutable snapshot */ }
    public void Reset() { /* Interlocked.Exchange totals to 0; clear dict */ }

    private sealed class AdapterStats { /* mutable counters with Interlocked guards */ }
}
```

### 3. `LlmTelemetrySnapshot` — immutable point-in-time read

`WebReaper.AI/Llm/LlmTelemetrySnapshot.cs`:

```csharp
public sealed record LlmTelemetrySnapshot(
    long CallCount,
    long? InputTokens,
    long? OutputTokens,
    long? CachedInputTokens,
    long? TotalTokens,
    long ParseRetries,
    TimeSpan TotalDuration,
    IReadOnlyDictionary<string, LlmAdapterStats> PerAdapter)
{
    public static readonly LlmTelemetrySnapshot Empty = new(
        CallCount: 0,
        InputTokens: null, OutputTokens: null,
        CachedInputTokens: null, TotalTokens: null,
        ParseRetries: 0, TotalDuration: TimeSpan.Zero,
        PerAdapter: new Dictionary<string, LlmAdapterStats>());
}

public sealed record LlmAdapterStats(
    string Name,
    long CallCount,
    long? InputTokens,
    long? OutputTokens,
    long? CachedInputTokens,
    long? TotalTokens,
    long ParseRetries,
    TimeSpan TotalDuration);
```

`null` tokens propagate: if any single recorded `LlmCallUsage` had
`null` for a field, the snapshot sums the non-null subset and surfaces
the value (callers can check whether the sum is partial via
`CallCount` vs. how many calls actually carried tokens — a future
field could distinguish). For v1, partial sums are documented; the
typical case is all-or-none per provider.

### 4. `RunReport` + `RunTelemetryHooks` — core engine-level surface

Two core records in `WebReaper/Domain/Telemetry/` (new folder).

**`RunReport.cs`** — the returned summary:

```csharp
namespace WebReaper.Domain.Telemetry;

/// <summary>
/// Per-run summary returned by <see cref="WebReaper.Core.ScraperEngine.RunAsync"/>
/// and via <see cref="WebReaper.Domain.Agent.AgentResult.Report"/>.
/// <para>
/// The <see cref="Llm"/> field is typed as <see cref="object"/> in core
/// to keep the satellite quarantine (ADR-0009) — consumers cast to
/// <c>WebReaper.AI.Llm.LlmTelemetrySnapshot</c> when the
/// <c>WebReaper.AI</c> satellite is referenced. Core does not take
/// the AI dep just to type a return.
/// </para>
/// </summary>
/// <param name="Llm">Opaque AI telemetry snapshot — null when no LLM
/// adapter ran on this engine. Cast to
/// <c>WebReaper.AI.Llm.LlmTelemetrySnapshot</c> when AI satellite is
/// in use.</param>
/// <param name="Duration">Wall-clock time from <c>RunAsync</c> entry
/// to completion.</param>
public sealed record RunReport(
    object? Llm,
    TimeSpan Duration);
```

**`RunTelemetryHooks.cs`** — the satellite-clean ctor channel into
the engines:

```csharp
namespace WebReaper.Domain.Telemetry;

/// <summary>
/// The core-side surface satellites use to plug per-run telemetry into
/// <see cref="WebReaper.Core.ScraperEngine"/> and the agent engine
/// without core taking a dep on the satellite-defined snapshot shapes
/// (ADR-0009 quarantine). The satellite-side builder constructs the
/// record once at <c>BuildAsync</c> time over its <c>LlmCallTelemetry</c>
/// instance; the engine treats every callback as opaque, calling
/// <see cref="Snapshot"/> at the end of <c>RunAsync</c>, <see cref="Reset"/>
/// at the start, and <see cref="TotalLlmTokens"/> at every step of the
/// agent loop for the <c>MaxBudgetTokens</c> check.
/// <para>
/// Default field semantics: <see cref="TotalLlmTokens"/> is nullable
/// because not every satellite tracks LLM tokens — a satellite that
/// reports network bytes / page count instead leaves it null.
/// </para>
/// </summary>
public sealed record RunTelemetryHooks(
    Func<object?> Snapshot,
    Action Reset,
    Func<long?>? TotalLlmTokens = null);
```

Both records live in core; `RunReport.Llm` is `object?` (cast at the
consumer) and the hooks' `Snapshot` returns `object?` (the satellite
wraps its concrete snapshot type). Same ADR-0009 posture as
`AgentRunSnapshot.LastOutcome` reaching into satellite-defined
outcome types via core's `AgentDecisionOutcome` — except the LLM
telemetry types live entirely in the satellite, so the typing has to
soften at the core boundary.

Alternative considered (Fork 4): move `LlmTelemetrySnapshot` into
core. Rejected — would pull `LlmCallUsage` + descriptor-name
attribution into core, which implies LLM-shape knowledge there.
Alternative considered (Fork 11): an `IRunTelemetry` interface
instead of the `RunTelemetryHooks` record. Rejected for parity with
the project's record-heavy style (`LlmCallResult`, `AgentDecision`,
`CrawlOutcome` — all sealed records). The record is constructable in
one expression by the satellite; an interface would require a stub
class.

### 5. `LlmCall<TResponse>` ctor gains `ILlmCallTelemetry?`

`WebReaper.AI/Llm/LlmCall.cs` constructor:

```csharp
public LlmCall(
    IChatClient chatClient,
    LlmCallDescriptor<TResponse> descriptor,
    ILlmCallTelemetry? telemetry = null,       // ← new; default null
    ILogger<LlmCall<TResponse>>? logger = null)
{
    // ... existing arg checks ...
    _telemetry = telemetry ?? NullLlmCallTelemetry.Instance;
}
```

`InvokeAsync` reports on every successful return path (the two
parse-success arms — first-shot and retry) BEFORE returning:

```csharp
var sw = Stopwatch.StartNew();
// ... existing call + parse logic ...
sw.Stop();
_telemetry.Record(new LlmCallUsage(
    DescriptorName: _descriptor.Name,
    InputTokens: input,
    OutputTokens: output,
    CachedInputTokens: cached,
    TotalTokens: total,
    ParseRetries: retries,
    Duration: sw.Elapsed));
return new LlmCallResult<TResponse>(value, input, output, cached, total, raw, retries);
```

The parse-failure-after-retry path (which throws `LlmCallException`)
also reports — with token counts captured from the two failed
attempts; the descriptor name and retry count are still useful
observability. The failure throws AFTER the report.

### 6. The four `Llm*` adapters gain `ILlmCallTelemetry?`

Each adapter (`LlmContentExtractor`, `LlmSelectorRepairer`,
`LlmActionResolver`, `LlmAgentBrain`) takes an optional
`ILlmCallTelemetry?` ctor arg, threads it to the `LlmCall<T>`
constructor. Public ctor signatures grow by one nullable parameter
(non-breaking when defaulted).

Example, `LlmContentExtractor`:

```csharp
public LlmContentExtractor(
    IChatClient chatClient,
    LlmExtractorOptions? options = null,
    ILlmCallTelemetry? telemetry = null)         // ← new
{
    _options = options ?? new LlmExtractorOptions();
    _call = new LlmCall<JsonObject>(chatClient,
        descriptor: BuildDescriptor(_options),
        telemetry: telemetry);                   // ← thread through
}
```

### 7. Builder wiring — telemetry is per-builder, passed to engine + adapters

`ScraperEngineBuilder` + `AgentEngineBuilder` each gain an internal
field:

```csharp
internal ILlmCallTelemetry? _llmTelemetry;

internal ILlmCallTelemetry GetOrCreateLlmTelemetry()
    => _llmTelemetry ??= new LlmCallTelemetry();
```

Each `WithLlm*` extension (`LlmExtractorRegistration.WithLlmExtractor` /
`.WithLlmFallback` / `.WithLlmSelfHealing`,
`LlmActionResolverRegistration.WithLlmActionResolver`,
`LlmAgentBrainRegistration.WithLlmBrain`) retrieves the telemetry and
passes it to the adapter ctor:

```csharp
public static ScraperEngineBuilder WithLlmExtractor(
    this ScraperEngineBuilder builder,
    IChatClient chatClient,
    LlmExtractorOptions? options = null)
{
    var telemetry = builder.GetOrCreateLlmTelemetry();      // ← new
    return builder.WithExtractor(
        new LlmContentExtractor(chatClient, options, telemetry));
}
```

`BuildAsync` passes `_llmTelemetry` (which may still be null — no
AI registered) to the engine constructor. Engine stores it; uses
`NullLlmCallTelemetry.Instance` when null.

### 8. `ScraperEngine.RunAsync` returns `Task<RunReport>`

Engine ctor gains one new optional arg:

```csharp
internal ScraperEngine(
    // ... existing args ...
    RunTelemetryHooks? telemetryHooks = null)
```

Signature change on `RunAsync`:

```csharp
// Before:
public async Task RunAsync(CancellationToken cancellationToken = default)

// After:
public async Task<RunReport> RunAsync(CancellationToken cancellationToken = default)
```

Implementation:

```csharp
public async Task<RunReport> RunAsync(CancellationToken cancellationToken = default)
{
    _telemetryHooks?.Reset();
    var sw = Stopwatch.StartNew();
    try
    {
        // ... existing body unchanged ...
    }
    finally
    {
        sw.Stop();
    }
    return new RunReport(
        Llm: _telemetryHooks?.Snapshot(),   // object? — satellite cast at consumer
        Duration: sw.Elapsed);
}
```

Caller compatibility: `await engine.RunAsync(ct)` works unchanged
(C# discards the `Task<T>` result). A caller that wants the report
writes `var report = await engine.RunAsync(ct)`.

### 9. `AgentResult` grows a `RunReport` field

Positional record evolution — append `RunReport` field. Follows the
ADR-0061 / `AgentRunSnapshot.LastOutcome` pattern (defensive default
for any deconstructor pinned to the old arity):

```csharp
public sealed record AgentResult(
    string RunId,
    IReadOnlyList<JsonObject> Records,
    string TerminationReason,
    IReadOnlyList<AgentDecision> History,
    IReadOnlyList<string> VisitedUrls,
    int StepsExecuted,
    RunReport Report);                           // ← new
```

`AgentRunSnapshot` does NOT gain the report — telemetry is per-attempt,
not persisted across resumes (a resumed run's accumulator starts
fresh; the original run's report is gone unless captured by the
caller). Documented.

### 10. `AgentEngineOptions.MaxBudgetTokens` enforcement

`AgentEngine` ctor gains the same `RunTelemetryHooks? telemetryHooks`
arg; the loop checks after every step, before the next brain call.
Termination precedence matches ADR-0051 fork 6:

```csharp
// Inside the while(true) loop, after step increment, before next iteration:
if (_options.MaxBudgetTokens is long cap
    && _telemetryHooks?.TotalLlmTokens?.Invoke() is long spent
    && spent >= cap)
{
    terminationReason =
        $"MaxBudgetTokens ({cap}) reached (spent={spent})";
    _logger.LogInformation(
        "Agent run {RunId} stopping: {Reason}", runId, terminationReason);
    break;
}
```

The chained-pattern reads the hook's nullable getter (which is itself
optional — a satellite that doesn't track tokens leaves it null,
making the cap silently inert, matching the documented behaviour on
chat clients that don't surface usage). The `TerminationReason`
already documents `"MaxBudgetTokens reached"` in the `AgentResult`
XML doc — this ADR finally makes that documented behaviour real.

### 11. `LlmCallTelemetry`'s thread-safety

`ScraperEngine` runs `Parallel.ForEachAsync` with
`MaxDegreeOfParallelism` workers — each worker's job may transitively
invoke an LLM (through a self-healing extractor, a fallback router,
or an LLM-using page processor). `LlmCallTelemetry.Record` is hot —
all token-total accumulators use `Interlocked.Add`; the per-adapter
dictionary uses `ConcurrentDictionary.AddOrUpdate`. The per-adapter
`AdapterStats` inner mutable holds its own per-field `Interlocked`
counters. `Snapshot()` reads each total atomically; the cross-field
read is not transactional (a single snapshot may see input-token-count
N1 from one moment and output-token-count N2 from a moment later) —
acceptable: snapshots are statistical, not balance-sheet.

## Considered options

### Fork 1 — How telemetry flows from `LlmCall<T>` to engine

| Option | What | Verdict |
|---|---|---|
| (a) Constructor inject `ILlmCallTelemetry` into `LlmCall<T>` | Explicit; per-role attribution via `descriptor.Name`; testable. | **Recommended.** Matches the ADR-0059 descriptor + mechanism pattern; the four adapters already wire `LlmCall<T>` ctors so threading one more nullable arg is mechanical. |
| (b) `AsyncLocal<LlmCallTelemetry>` accumulator | Engine sets; `LlmCall<T>` reads ambiently. | Rejected. Spooky-action-at-a-distance; `Parallel.ForEachAsync`'s thread hops should preserve `AsyncLocal` via `ExecutionContext`, but the failure mode is silent (lost reports). Explicit injection is the project's stated DI shape. |
| (c) `IChatClient` decorator (M.E.AI middleware pattern) | Wrap the client; intercept `GetResponseAsync`; read `Usage`. | Rejected for per-role attribution — the decorator sees the client, not the descriptor. M.E.AI's own `UseOpenTelemetry` is the right tool for aggregate-only telemetry; the per-adapter breakdown needs `descriptor.Name`. Could compose (a consumer who wants OTel-aggregate + per-role wires both). |
| (d) Static event on `LlmCall<T>` | `LlmCall<T>.CallCompleted += handler` (instance event). | Rejected. Static events leak across runs; instance events require the engine to subscribe to every adapter's call instance — clumsy. |

### Fork 2 — Engine `RunAsync` return type

| Option | What | Verdict |
|---|---|---|
| (a) Change `ScraperEngine.RunAsync` from `Task` → `Task<RunReport>` | Symmetric with `AgentEngine.RunAsync` (already returns `Task<AgentResult>`); pre-tag so no v10 consumers exist. | **Recommended.** `await engine.RunAsync(ct)` is unchanged; only explicit `Task` typing breaks (rare). Examples updated in the same PR. |
| (b) Add property `ScraperEngine.LastRunReport` set after RunAsync | Mutate-on-completion. | Rejected. Mutable post-run property is the wrong shape for an immutable result; opens a "report from which run?" ambiguity if `RunAsync` is called multiple times. |
| (c) Event-based: `ScraperEngine.RunCompleted` event | Subscribe before running; receive report on completion. | Rejected. Subscription ceremony for a single-fire signal; the return-type change is the natural shape. |
| (d) Add overload `RunWithReportAsync()` returning `Task<RunReport>`; keep `RunAsync` as `Task` | Non-breaking; consumers opt in. | Rejected. Two methods for the same operation; encourages confusion. Pre-tag is the right moment to consolidate. |

### Fork 3 — Per-adapter attribution

| Option | What | Verdict |
|---|---|---|
| (a) Per-`descriptor.Name` keying | `LlmCallUsage.DescriptorName = _descriptor.Name`; accumulator dictionary-keyed by it. | **Recommended.** Cheap (the name field already exists — ADR-0059); useful (consumer sees brain vs extractor split); reusable (consumer-authored adapters using `LlmCall<T>` pick their own name and get attribution for free). |
| (b) Aggregate-only | One bucket; no breakdown. | Rejected. Loses the highest-value optimisation signal — which adapter is the cost driver. |
| (c) Per-page-per-adapter | Per-`(URL, DescriptorName)` dict. | Rejected (v2). Requires URL context propagation through `LlmCall<T>`; doable but disproportionate for v1. |

### Fork 4 — `RunReport` location: core vs satellite

| Option | What | Verdict |
|---|---|---|
| (a) `RunReport` in core; `LlmTelemetrySnapshot` in satellite; `RunReport.Llm` typed as `object?` | Core types the return; satellite types the AI specifics; consumer casts. | **Recommended.** Honours the ADR-0009 quarantine — core stays AI-clean. The `object?` cast is one line at the consumer (`(LlmTelemetrySnapshot?)report.Llm`) and matches how MongoDB drivers, JSON .NET, and analytics SDKs surface opaque payloads. |
| (b) `RunReport` + `LlmTelemetrySnapshot` both in core | Move LLM-shape knowledge into core. | Rejected. Pulls `LlmCallUsage` and the four adapter names' worth of context into core; violates ADR-0009. |
| (c) `RunReport` in satellite; `ScraperEngine.RunAsync` returns satellite type | Core engine references satellite. | Rejected. Inverts the dep direction — core would import satellite. |
| (d) Two return types — `RunReport` in core, satellite extension method `report.LlmSnapshot()` | Hides the cast behind an extension. | Considered. Implementation-detail call: ship plain `object?` for v1 (simpler); add extension if the cast is awkward in practice. |

### Fork 5 — Per-run telemetry isolation across builder re-use

| Option | What | Verdict |
|---|---|---|
| (a) One telemetry per builder; engine `Reset()`s at start of `RunAsync` | Adapters wire once at builder time; accumulator resets per run. | **Recommended.** Re-using a builder is unusual but supported; reset isolates runs; cheap (a few `Interlocked.Exchange`). |
| (b) Fresh telemetry per `BuildAsync` | Each engine gets its own instance. | Rejected. Adapters are constructed at `WithLlm*` time (before `BuildAsync`); fresh telemetry per-build would require re-wiring them, which is brittle. |
| (c) Fresh telemetry per `RunAsync` | Engine constructs new instance per call. | Rejected. Adapters' telemetry references would be stale; would need a re-bind seam. Over-engineered. |

### Fork 6 — `AgentResult` evolution: append vs. new property record

| Option | What | Verdict |
|---|---|---|
| (a) Append `RunReport Report` to positional ctor (7-field shape) | Record evolution with default semantics. | **Recommended.** Matches ADR-0061's `AgentRunSnapshot.LastOutcome` evolution pattern. Deconstructors pinned to 6-arity break; named-property reads continue unchanged. Pre-tag — acceptable. |
| (b) New side-by-side record `AgentResultV2(AgentResult original, RunReport report)` | Wrap the existing result. | Rejected. Two types for one concept; consumers always reach through; pointless indirection. |
| (c) Settable `Report` property on `AgentResult` | Make the record mutable in this field. | Rejected. Records are immutable by design; mutation defeats the audit-trail intent. |

### Fork 7 — MaxBudgetTokens enforcement point

| Option | What | Verdict |
|---|---|---|
| (a) Check between iterations of the agent loop (after step++, before next brain call) | Matches ADR-0051 fork 6 termination precedence (Stop → MaxSteps → MaxBudget → cancel). | **Recommended.** Clear precedence; one check per step is cheap; the loop terminates cleanly with a documented reason string. |
| (b) Throw `BudgetExceededException` from `LlmCall<T>` when cumulative crosses cap | Hard fail at the call site. | Rejected. Exception-as-control-flow; the loop should terminate, not throw. ADR-0051's stop-rule pattern is "verdict-as-value." |
| (c) Don't enforce; leave `MaxBudgetTokens` documented but inert | Status quo. | Rejected. The whole point of this ADR is to close documented-but-broken behaviour. |

### Fork 8 — `LlmCallTelemetry` thread-safety choice

| Option | What | Verdict |
|---|---|---|
| (a) `Interlocked.Add` on aggregate totals + `ConcurrentDictionary` for per-adapter | Lock-free fast path; weak read consistency across fields. | **Recommended.** Snapshots are statistical, not transactional. The `Parallel.ForEachAsync` hot path stays uncontended. |
| (b) `lock` around the whole accumulator | Strong consistency. | Rejected. Serialises all parallel workers through one lock; performance cliff under parallelism > 1. |
| (c) `ImmutableInterlocked` swap of the whole state | Stronger atomicity; CAS retries under contention. | Rejected. Allocation per Record; defeats the cheap-path optimisation. |

### Fork 9 — Streaming snapshot mid-run

| Option | What | Verdict |
|---|---|---|
| (a) Final-only — `RunReport` returned at end | Single payload. | **Recommended (v1).** Simpler; the accumulator is already snapshot-safe so v2 can add a property without re-shaping. |
| (b) `engine.GetSnapshot()` mid-run | Live observability. | Rejected (v2 deferral). Useful but not load-bearing for v1; the underlying machinery supports it. |
| (c) `IObservable<LlmCallUsage>` from the engine | Reactive stream. | Rejected. Pulls a Rx dep into the core. Anyone wanting reactive composes their own `ILlmCallTelemetry` impl. |

### Fork 10 — Cost (USD) computation

| Option | What | Verdict |
|---|---|---|
| (a) Tokens only; consumer maps to cost | No pricing in library. | **Recommended.** Pricing changes monthly; locking a table in ages out fast. The library stays correct. |
| (b) Engine ships a pricing table → estimated USD | Convenience field. | Rejected. Library staleness liability; per-tenant negotiated rates make it wrong for many consumers anyway. |
| (c) Pluggable `IPriceTable` seam | Consumer-supplied pricing. | Rejected (v2). Useful but optional — a consumer who wants USD multiplies tokens themselves; the seam is a v2 question. |

### Fork 11 — Core/satellite boundary shape

| Option | What | Verdict |
|---|---|---|
| (a) `RunTelemetryHooks` record — one ctor arg per engine | Three delegates bundled in a sealed record in `WebReaper.Domain.Telemetry`. Satellite constructs once at `BuildAsync` time. | **Recommended.** Matches the project's record-heavy style (`LlmCallResult`, `AgentDecision`, `CrawlOutcome` — all sealed records); engine ctor signature stays clean (one arg, not three); future telemetry additions are non-breaking via default-valued fields on the record. |
| (b) Three loose `Func`/`Action` delegate ctor args | `Func<object?>?` + `Action?` + `Func<long?>?` directly on each engine ctor. | Rejected. `ScraperEngine` already has 10 ctor args, `AgentEngine` has 13 — three more pushes both into "wall of args" territory. The bundling is cheap, the readability win is real. |
| (c) `IRunTelemetry` interface | One ctor arg per engine, but an interface contract. | Rejected. The hook record needs no swap-at-runtime polymorphism — it's constructed once by the builder and consumed by the engine. An interface would require a stub class for tests (vs. the record's one-expression construction); records also pattern-match cleaner in C# 12+. |
| (d) Have core take the satellite-side `ILlmCallTelemetry` directly | Cleanest API but pulls AI types into core. | Rejected. Violates ADR-0009 quarantine — core must not see satellite types. The `object?` opacity is the price of the quarantine. |

## Consequences

- **Consumers can see what their crawl cost.** `var report = await
  engine.RunAsync(ct)` gives input/output/cached tokens, per-adapter
  breakdown, duration. Paired with ADR-0065, the cached-vs-uncached
  split shows whether the cache hint paid off.
- **`MaxBudgetTokens` works.** A documented-since-ADR-0051 safety
  knob finally enforces. Agent runs that hit the cap terminate with
  `TerminationReason = "MaxBudgetTokens (X) reached (spent=Y)"`.
- **`ScraperEngine.RunAsync` return type changes.** `Task` →
  `Task<RunReport>`. `await engine.RunAsync(ct)` (discard) keeps
  working; explicit `Task` typing breaks — pre-tag, the affected
  examples are in-tree and updated in the same PR.
- **`AgentResult` 6-field → 7-field positional ctor.** Deconstructors
  pinned to the old arity break (rare); named-property access
  unchanged. Same pattern as ADR-0061 for `AgentRunSnapshot`.
- **Consumer-authored adapters get telemetry for free.** Any adapter
  using `LlmCall<T>` with the registered `ILlmCallTelemetry` produces
  attributed usage. The descriptor's `Name` is the only thing the
  consumer picks.
- **Per-page attribution and USD cost remain unsolved.** Documented
  as v2 deferrals. The current design doesn't preclude them.
- **No core AI dep introduced.** `RunReport.Llm` is `object?` —
  the satellite owns the concrete type. Consumers cast when reading.
- **The `WebReaper.AI` satellite grows three public types**
  (`ILlmCallTelemetry`, `LlmCallTelemetry`, `LlmCallUsage`) and one
  public snapshot record (`LlmTelemetrySnapshot` +
  `LlmAdapterStats`). Stable surface.
- **CONTEXT.md** gains **LLM call telemetry** + **Run report** terms
  in the AI-native and Core sections respectively.
- **CLAUDE.md** gets two gotcha lines: (1) `RunReport.Llm` is
  `object?` — cast to `LlmTelemetrySnapshot` when the AI satellite
  is referenced; (2) `AgentResult` now has 7 fields — deconstructor
  positional reads break, named property reads unchanged.

## Bounded scope (v1)

- **No cost (USD) computation.** Fork 10.
- **No per-page attribution.** Fork 3 (c).
- **No streaming mid-run snapshot.** Fork 9 (b).
- **No scraper-side budget cap.** No `ScraperConfig.MaxBudgetTokens`
  field; deferred.
- **No persisted telemetry across agent resume.**
  `AgentRunSnapshot` doesn't carry the report; resume accumulator
  starts fresh.

## Implementation (slice, when accepted)

**Satellite — three new files, four edits:**

1. **`WebReaper.AI/Llm/LlmCallUsage.cs`** — new public record.
2. **`WebReaper.AI/Llm/ILlmCallTelemetry.cs`** — new interface +
   `NullLlmCallTelemetry`.
3. **`WebReaper.AI/Llm/LlmCallTelemetry.cs`** — default impl with
   thread-safe accumulator + `LlmTelemetrySnapshot` +
   `LlmAdapterStats` records.
4. **`WebReaper.AI/Llm/LlmCall.cs`** — ctor gains
   `ILlmCallTelemetry?`; `InvokeAsync` reports on success arms and
   after parse-failure-after-retry (before throw).
5. **`WebReaper.AI/LlmContentExtractor.cs`** —
   **`LlmSelectorRepairer.cs`** — **`LlmActionResolver.cs`** —
   **`LlmAgentBrain.cs`** — each ctor gains optional
   `ILlmCallTelemetry?`; threaded to `LlmCall<T>`.
6. **`WebReaper.AI/LlmExtractorRegistration.cs`** —
   **`LlmActionResolverRegistration.cs`** —
   **`LlmAgentBrainRegistration.cs`** —
   **`UseAiRegistration.cs`** — each `WithLlm*` /
   `.UseAi` retrieves the builder's telemetry and passes through.

**Core — two new files, three edits:**

7. **`WebReaper/Domain/Telemetry/RunReport.cs`** + **`RunTelemetryHooks.cs`**
   — two new public records in a new folder. `using
   WebReaper.Domain.Telemetry;` is the import pattern (parallel to
   `WebReaper.Domain.Agent`).
8. **`WebReaper/Core/ScraperEngine.cs`** — ctor gains one optional
   arg `RunTelemetryHooks? telemetryHooks = null`; field stored;
   `RunAsync` resets + snapshots through it. Core has no
   `ILlmCallTelemetry` knowledge — the satellite-side builder
   constructs the hooks over its `LlmCallTelemetry` instance and
   passes them.

   ```csharp
   internal ScraperEngine(
       // ... existing 10 args ...
       RunTelemetryHooks? telemetryHooks = null)
   ```

9. **`WebReaper/Domain/Agent/AgentResult.cs`** — append `RunReport
   Report` field (6 → 7 positional fields).

10. **`WebReaper/Core/Agent/Concrete/AgentEngine.cs`** — ctor takes
    the same `RunTelemetryHooks?`; `RunAsync` resets at start,
    snapshots at end, returns `AgentResult` with the populated
    `Report`. Loop checks `MaxBudgetTokens` via
    `telemetryHooks?.TotalLlmTokens?.Invoke()`.

11. **`WebReaper/Builders/ScraperEngineBuilder.cs`** +
    **`WebReaper/Builders/AgentEngineBuilder.cs`** — each adds:
    - `internal object? LlmTelemetry { get; set; }` — typed
      `object?` to keep core AI-clean. The satellite-side `WithLlm*`
      extensions cast it to `ILlmCallTelemetry` when reading /
      writing. Same pattern ADR-0058 used for `_ownedDisposables` —
      satellites stash typed handles on the builder via internal
      accessors.
    - `BuildAsync` constructs `RunTelemetryHooks` over the
      satellite-side `LlmCallTelemetry` (if set) and passes to the
      engine ctor. The construction lives in a tiny core helper or
      inline — straightforward either way.

    The satellite's `Llm*Registration.WithLlm*` extensions retrieve /
    initialise the telemetry via a satellite-side extension method
    `builder.GetOrCreateLlmTelemetry()` that does the cast and
    null-coalesce. No new generic "scratchpad bag" introduced — the
    one-field handle is enough for this ADR; if a future satellite
    needs another handle, it adds its own field.

**Tests — `WebReaper.Tests/WebReaper.AI.Tests/`:**

12. **`LlmCallTelemetryTests.cs`** (new):
    - Empty snapshot has zero totals, empty per-adapter dict.
    - Single `Record` increments `CallCount`, populates totals.
    - Two records sum: `InputTokens`, `OutputTokens`, etc.
    - Null tokens in some records still produce a sum (the non-null
      subset) — documented partial-sum behaviour.
    - Per-adapter breakdown: two records with different `DescriptorName`
      land in separate per-adapter buckets.
    - `Reset` clears totals and per-adapter dict.
    - Parallel `Record` from 100 tasks × 100 records each →
      `CallCount == 10_000`; totals correct.
13. **`LlmCallTelemetryWiringTests.cs`** (new):
    - `LlmContentExtractor` with telemetry reports
      `DescriptorName == "LlmContentExtractor"`.
    - Same for the other three adapters.
    - `LlmCall<T>` with `telemetry: null` defaults to
      `NullLlmCallTelemetry` (verifies no NRE).
14. **`UseAiTelemetryTests.cs`** (new):
    - `.UseAi(client)` wires the same telemetry instance to extractor,
      repairer, resolver, brain.
    - Builder's `WithLlmExtractor` à la carte creates telemetry on
      first call; reused on second.

**Tests — `WebReaper.Tests/WebReaper.UnitTests/`:**

15. **`ScraperEngineRunReportTests.cs`** (new):
    - `RunAsync` returns a `RunReport` with non-null `Duration` on
      a no-op crawl.
    - `RunReport.Llm` is null when no LLM adapter is registered.
    - Two consecutive `RunAsync` calls each return their own report
      (verifies telemetry `Reset` between runs).
16. **`AgentEngineMaxBudgetTokensTests.cs`** (new):
    - Brain that runs forever; `MaxBudgetTokens = 100`; stub
      telemetry that reports 50 / 100 / 150 across three steps;
      assert termination at the 150-step with
      `TerminationReason = "MaxBudgetTokens (100) reached (spent=150)"`.
    - `MaxBudgetTokens = null` (default) — no termination from this
      cause; the existing MaxSteps cap still applies.

**Docs:**

17. **CONTEXT.md** — gains **LLM call telemetry** (satellite term)
    + **Run report** (core term); relationship line: *mechanism
    (records) → telemetry (accumulates) → engine (returns)*.
18. **CLAUDE.md** — two gotcha lines:
    - `RunReport.Llm` is `object?` to keep the ADR-0009 satellite
      quarantine; cast to `WebReaper.AI.Llm.LlmTelemetrySnapshot`
      when the AI satellite is in use.
    - `AgentResult` is now a 7-field record (added `Report`); old
      positional deconstructors pinned to the 6-arity break;
      named-property reads are unchanged. ADR-0061's
      `AgentRunSnapshot.LastOutcome` pattern.
    - `AgentEngineOptions.MaxBudgetTokens` is now enforced (was
      documented-but-inert); termination precedence is
      `Stop → MaxSteps → MaxBudgetTokens → cancellation`.

**Examples (in-tree) — one-line updates:**

19. **`Examples/WebReaper.ConsoleApplication/Program.cs`** + any
    other consumer of `await engine.RunAsync(...)` — if pinned to
    `Task` typing or `Func<Task>`, update. Most consumers use
    `await engine.RunAsync(ct)` directly, which is forwards-compatible.

### Guardrails (when implemented)

- `dotnet build WebReaper.sln` — 0 errors. The
  `RunTelemetryHooks` record (in core) is the AI-clean ctor channel;
  satellite-side construction is one expression at `BuildAsync` time.
- `dotnet test WebReaper.Tests/WebReaper.UnitTests` — existing tests
  pass; new `ScraperEngineRunReportTests` +
  `AgentEngineMaxBudgetTokensTests` pass.
- `dotnet test WebReaper.Tests/WebReaper.AI.Tests` — existing tests
  pass; new telemetry tests pass; ADR-0065's caching tests still pass.
- `WebReaper.AotSmokeTest` — unchanged. The hooks' `Func<object?>` +
  `Func<long?>?` are AOT-friendly (no reflection-emit, no
  dynamic-dispatch).

## References

- ADR-0009 — registration seam + satellite pattern; the
  `RunTelemetryHooks` record (in core) preserves the satellite
  quarantine (`RunReport.Llm` is opaque to core; the hooks' callbacks
  return `object?`).
- ADR-0022 — Crawl driver; the `ScraperEngine.RunAsync` shape this
  ADR widens.
- ADR-0033 — `IAsyncInitializable`; the engine warms adapters on the
  way in; this ADR resets the telemetry on the same boundary.
- ADR-0051 — Agent driver; the `AgentResult` shape this ADR widens;
  `MaxBudgetTokens` finally enforced per fork 6 termination
  precedence.
- ADR-0058 — `IAsyncDisposable` on engines; the telemetry follows the
  same per-engine ownership pattern.
- ADR-0059 — `LlmCall<TResponse>` mechanism; the descriptor's
  `Name` field powers per-adapter attribution.
- ADR-0061 — `AgentRunSnapshot.LastOutcome` record evolution; the
  pattern `AgentResult.Report` follows.
- ADR-0064 — `.UseAi(...)` aggregator; the telemetry is wired once
  through it (and à la carte through `WithLlm*`).
- ADR-0065 — system-prompt caching + cached-token split; this ADR is
  the engine-level consumer of those fields.
