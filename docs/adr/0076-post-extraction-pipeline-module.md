# 0076. Post-Extraction Pipeline Module

- **Status:** Accepted — implementing (2026-05-29)
- **Date:** 2026-05-29
- **Deciders:** Alex (HITL), Claude (architecture pass)

## Context

The in-process **Crawl driver** (`ScraperEngine`) and the **Agent driver**
(`AgentEngine`) each re-implement the entire post-extraction surface that
ADR-0038 defines as Process-then-Emit: run a **Target page**'s **ParsedData**
through the ordered **Page processor** pipeline (the `Kept` / `Dropped`
**Page verdict** loop, with the swallow-and-log-on-throw policy of ADR-0029),
then fan the surviving record out to every **Sink** with a per-sink deep-clone
under `Task.WhenAll`.

- `ScraperEngine.ProcessTargetPage` (`WebReaper/Core/ScraperEngine.cs`) and
  `AgentEngine.RunProcessorsAsync` + `FanOutSinksAsync`
  (`WebReaper/Core/Agent/Concrete/AgentEngine.cs`) are two copies of the same
  logic. The fan-out (deep-clone per sink, `Task.WhenAll`) is byte-identical;
  the processor loop differs only in how `PageContext` is built and in log
  strings.

A latent correctness bug was discovered while scoping this: `AgentEngine` is a
plain `sealed class` (not `IAsyncDisposable`, no warm-up), so the **Sink**s and
**Page processor**s it holds are never warmed up (ADR-0033) and never disposed
(ADR-0058). A durable **Sink** on an **Agent run** — a `BufferedFileSink`
**File sink drain**, or a Redis/Cosmos satellite sink — therefore never receives
`InitializeAsync` and never gets its flush-on-dispose. Records can be silently
lost on agent runs. The same gap leaves the **Agent run store** (`_runStore`,
which may be a durable satellite adapter) unwarmed and undisposed.

## Decision

We will extract a public **Post-extraction pipeline** module (near
`WebReaper/Core/Processing/`) that owns the whole post-extraction surface for a
single **ParsedData**:

- A fused `ProcessAndEmitAsync(ParsedData) -> ParsedData?`: run the **Page
  processor** pipeline; on `Kept`, fan out to every **Sink** and return the
  surviving record; on `Dropped`, emit to no sink and return `null`. The
  drop-means-no-emit invariant lives in one place.
- The module **holds** the processors and sinks and owns their lifecycle: it
  implements `IAsyncInitializable` (ADR-0033) and `IAsyncDisposable` (ADR-0058)
  itself, so each driver warms and disposes it as one adapter slotted between
  the **Visited-link tracker** and the **Scheduler**. The ADR-0058 reverse
  order holds by composition (processors-then-sinks dispose inside the module).
- It takes an already-built **ParsedData** (the crawl side has one from the
  **Spider** with the ADR-0031 url-merge done; the agent builds one in a line),
  not `(url, json)` — taking the latter would force the crawl side to
  deconstruct and risk re-merging.

The **Crawl driver** ignores the return; the **Agent driver** uses it for its
run-scoped record bookkeeping (`records.Add` + `AgentDecisionOutcome.Extracted(
Record, RecordCount)`), where `RecordCount` is the cumulative run total and
stays the agent's concern. Three callers justify the public seam: the
in-process **Crawl driver**, the **Agent driver**, and the consumer-authored
distributed **Crawl driver** (ADR-0009), which today hand-re-derives the
deep-clone, the `Task.WhenAll`, and the dispose order.

As part of this, `AgentEngine` becomes a proper warm/dispose citizen
(`IAsyncInitializable` + `IAsyncDisposable`), with the **Agent run store** as
its own agent-local adapter — closing the durable-sink and run-store lifecycle
gap.

## Consequences

Good:
- One home for Process + Emit + their lifecycle. A new **Page verdict** arm, a
  metrics hook, or a change to clone semantics is edited once.
- The agent durable-sink / run-store lifecycle bug is fixed as a structural
  side effect of consolidation — there is nowhere for it to hide.
- The pipeline + fan-out + lifecycle assertions (processors-run-in-order,
  Drop-filters-the-page, throwing-processor-drops-not-aborts,
  each-sink-gets-its-own-clone, warm-up-once, dispose-flushes-in-reverse) move
  to the module's interface. Driver tests shrink to "a Target page record is
  routed into the module"; one new test pins the agent durable-sink flush.

Bad / costs:
- One new public type, and a contract that it runs after the **Spider** and
  before the driver enqueues children (implicit ordering in the driver loop).
- The distributed driver must opt in to gain the shared behaviour (it is
  consumer-authored; the public seam makes it a first-class caller, not a
  re-implementer, but does not auto-wire it).

## Alternatives considered

- **A pure per-record function** (stateless; drivers keep owning the
  processors+sinks and their lifecycle). Rejected: it leaves the same logic
  split across two ownership stories and forces a *separate* retrofit of
  `IAsyncInitializable`/`IAsyncDisposable` onto `AgentEngine` to fix the bug.
  The lifecycle-owning shape makes the fix fall out of consolidation.
- **Two separately-callable methods** (`RunPipeline` + `FanOut`). Rejected: it
  re-exposes the "don't emit a dropped record" invariant to every caller. No
  caller needs pipeline-without-emit or emit-without-pipeline — both collapse
  to the empty-collection case (zero processors, or zero sinks), which the
  fused call already handles. A cross-tier split (pipeline on worker A, emit on
  worker B) is the only thing that would need the two-method shape; it is in no
  ADR and consumer-authorable directly against `IPageProcessor` / `IScraperSink`
  if it ever lands.
- **Merge the two drivers into one execution engine.** Rejected: the **Agent
  driver** is sequential by design (ADR-0051) and the **Crawl driver** is
  parallel; only the post-extraction slice is shared, not the seeding/scheduling
  loop.
