# `AgentDecisionOutcome` + `AgentState.LastOutcome` — close the brain's feedback gap

## Status

**Proposed** (2026-05-24). Third ADR of the post-AI-native-wave
deepening campaign. Adds the brain's per-step *outcome* signal —
"what happened when the engine executed your last decision" — to the
bounded `AgentState` view. Composes with ADR-0062's `ISchemaValidator`
seam (a failed Extract becomes a structured outcome the brain sees
next step). Closed-sum addition; small surface, large behaviour
delta. Folds into the same v10.x release as the campaign.

## Context

The agent driver (ADR-0051) is sequential by design — every step's
decision depends on the previous step's outcome (fork 11). Yet the
brain sees no per-decision *outcome* in `AgentState`:

```csharp
public sealed record AgentState(
    string Goal,
    string CurrentUrl,
    string CurrentPageMarkdown,
    IReadOnlyList<string> CandidateUrls,
    IReadOnlyList<JsonObject> Extracted,   // every record so far
    IReadOnlyList<AgentDecision> History,  // every past decision
    IReadOnlyList<string> VisitedUrls,
    int StepNumber);
```

The brain sees `History` (the decisions it made) and `Extracted` (the
records pulled by Extract decisions); it does *not* see:

- Did the last `Extract` produce a record at all? (The page-processor
  pipeline may have dropped it via `PageVerdict.Dropped`.)
- Did the last `Extract` *satisfy the schema*? (ADR-0062's validator
  will say so; without an outcome there's nowhere to surface that.)
- Did the last `Follow` resolve to a real page, or a 404, or a redirect
  to login?
- Did the last `Act` actually dispatch? (Did the semantic-act resolver
  return a concrete arm, or did the cached arm fail and re-resolve?)
- Did the last decision *fail* mid-execution?

Without these signals the brain re-loops blindly. The two grounded
failure modes the field surface:

1. **Brain proposes the same `Follow(url)` twice because the first
   one 404'd and the brain doesn't know.** Engine logs the page-load
   failure and breaks the loop — the run terminates with
   `terminationReason = "page load failed: ..."`. The brain never got
   to re-decide; one bad URL ends the run.
2. **Brain proposes the same `Extract(schema)` because the first
   extract validated to zero fields.** The pipeline dropped the
   record (in ADR-0062 terms, validation failed); the brain sees
   `Extracted` *didn't grow* but has no signal *why*. Cost-runaway:
   the brain repeats the failing schema.

The fix is *one* closed-sum on `AgentState`. Same closed-sum lineage
as `AgentDecision` itself (ADR-0051), `PageAction` (ADR-0035),
`CrawlOutcome` (ADR-0001). The brain reads it, decides next.

### What goes in the outcome

Per-arm-shaped data — exactly what the brain needs to differentiate
"the last step succeeded" from "the last step failed how":

| Decision arm | Outcome shape | Brain-relevant facts |
|---|---|---|
| (None — first step) | `None` | The brain has no history. |
| `Extract` | `Extracted(JsonObject? Record, int RecordCount)` | The brain sees what it just produced (or `null` if dropped) + the running count. |
| `Follow` | `Followed(string ActualUrl, int StatusCode)` | The brain sees the *actual* URL (post-redirect) and the response code. |
| `Act` | `ActDispatched(PageAction ResolvedAction)` | The brain sees what its `Act` actually resolved to (SemanticAct → concrete arm). |
| (any) | `Failed(string Reason, string? ExceptionType)` | The brain sees the failure mode by class — distinguish timeout vs. parse-error vs. invalid-state without a full exception. |
| `Stop` (only when persisting end-state) | `Stopped(string Reason)` | The brain never sees this — the engine writes it once into the final snapshot for resumption inspection. |

`Record` on `Extracted` carries the *most recent* extract — capped at
one (the brain only needs the *last*; `Extracted` already has the
cumulative list). `ExceptionType` is the .NET type name only — the
full exception is logged separately; the brain only needs the *kind*.

### What this ADR is not

- **Not a new seam.** `AgentDecisionOutcome` is a closed-sum domain
  record alongside `AgentDecision`; the engine constructs it from the
  step's execution result.
- **Not a brain-protocol change.** `IAgentBrain.DecideAsync(state, ct)`
  stays the same — the state grows one field.
- **Not a History entry.** `History` records what the brain *decided*;
  `LastOutcome` records what *happened*. The two are causally
  ordered: `LastOutcome` refers to `History[^1]` (the last decision).

### Persistence

`AgentRunSnapshot` (ADR-0051's persistence shape) gains the field.
Resumable runs need it: when the engine resumes from
`LastDecidedStep + 1`, the brain's first decision on resume sees the
just-persisted *prior* outcome — the run picks up exactly where it
left off.

The snapshot version bump is structural (a new required field on the
record); satellite `IAgentRunStore` adapters use the same
JSON-round-trip codec (`AgentRunSnapshotCodec`) — adding a property is
forward-compatible (older snapshots are read as `LastOutcome = None`).

## Decision

Three pieces — one new closed-sum record, one new field on
`AgentState` + `AgentRunSnapshot`, one engine-side wire-up.

### 1. `AgentDecisionOutcome` — new closed sum

`WebReaper/Domain/Agent/AgentDecisionOutcome.cs`. Public abstract record
with private ctor, six nested sealed-record arms:

```csharp
public abstract record AgentDecisionOutcome
{
    private AgentDecisionOutcome() { }

    /// <summary>First step — no prior decision was executed. The brain's
    /// first DecideAsync sees this.</summary>
    public sealed record None() : AgentDecisionOutcome;

    /// <summary>The prior step's Extract decision produced a record and
    /// it passed every page processor (including ADR-0062's
    /// <c>ISchemaValidator</c>); the <paramref name="Record"/> is the
    /// most-recently-emitted JsonObject (or null when the processor
    /// pipeline dropped it). <paramref name="RecordCount"/> is the
    /// cumulative count of emitted records up to and including this
    /// step.</summary>
    public sealed record Extracted(JsonObject? Record, int RecordCount) : AgentDecisionOutcome;

    /// <summary>The prior step's Follow decision loaded a page;
    /// <paramref name="ActualUrl"/> is the post-redirect URL the page
    /// loader settled on; <paramref name="StatusCode"/> is the HTTP
    /// status (200 for ok, 404 for not-found, ...). 0 when the page
    /// type is Dynamic (the browser transport doesn't surface a single
    /// status code per page).</summary>
    public sealed record Followed(string ActualUrl, int StatusCode) : AgentDecisionOutcome;

    /// <summary>The prior step's Act decision dispatched a page action;
    /// <paramref name="ResolvedAction"/> is the concrete arm that ran
    /// (a SemanticAct resolves to a concrete Click / Wait / etc. via
    /// ADR-0050's resolver — the brain sees what its intent became).</summary>
    public sealed record ActDispatched(PageAction ResolvedAction) : AgentDecisionOutcome;

    /// <summary>The prior step failed mid-execution. <paramref name="Reason"/>
    /// is a human-readable summary; <paramref name="ExceptionType"/> is the
    /// .NET type name (e.g. <c>HttpRequestException</c>,
    /// <c>SemanticActResolutionException</c>) so the brain can branch on
    /// failure class without a full exception object.</summary>
    public sealed record Failed(string Reason, string? ExceptionType) : AgentDecisionOutcome;

    /// <summary>End-state marker — written to the final snapshot when the
    /// engine breaks out of the loop. The brain never sees this in
    /// <see cref="AgentState"/> (the loop has already terminated);
    /// resume tooling reads it.</summary>
    public sealed record Stopped(string Reason) : AgentDecisionOutcome;
}
```

### 2. `AgentState.LastOutcome` and `AgentRunSnapshot.LastOutcome`

Both records get one new property. Default `AgentDecisionOutcome.None`.

```csharp
public sealed record AgentState(
    string Goal,
    string CurrentUrl,
    string CurrentPageMarkdown,
    IReadOnlyList<string> CandidateUrls,
    IReadOnlyList<JsonObject> Extracted,
    IReadOnlyList<AgentDecision> History,
    IReadOnlyList<string> VisitedUrls,
    int StepNumber,
    AgentDecisionOutcome LastOutcome);
```

```csharp
public sealed record AgentRunSnapshot(
    string Goal,
    int LastDecidedStep,
    IReadOnlyList<AgentDecision> History,
    IReadOnlyList<string> VisitedUrls,
    IReadOnlyList<JsonObject> Records,
    string? CurrentUrl,
    AgentDecisionOutcome LastOutcome);
```

### 3. Engine populates from the prior step's execution

`AgentEngine.RunAsync` (ADR-0051) gains an `AgentDecisionOutcome
lastOutcome = new AgentDecisionOutcome.None()` local. After each
decision's switch arm executes, it sets `lastOutcome` to the
arm-shaped outcome:

```csharp
switch (decision)
{
    case AgentDecision.Stop:
        lastOutcome = new AgentDecisionOutcome.Stopped(decision.Reason);
        terminationReason = decision.Reason;
        goto Done;

    case AgentDecision.Extract extract:
        try
        {
            var extracted = await _contentExtractor.ExtractAsync(pageHtml, extract.Schema);
            // ADR-0062: validator runs here (in pipeline or as a processor)
            var processed = await RunProcessorsAsync(...);
            if (processed is not null)
            {
                records.Add(processed.Data);
                await FanOutSinksAsync(processed, cancellationToken);
                lastOutcome = new AgentDecisionOutcome.Extracted(
                    Record: processed.Data,
                    RecordCount: records.Count);
            }
            else
            {
                lastOutcome = new AgentDecisionOutcome.Extracted(
                    Record: null,
                    RecordCount: records.Count);
            }
        }
        catch (Exception ex)
        {
            lastOutcome = new AgentDecisionOutcome.Failed(
                Reason: $"Extract failed: {ex.Message}",
                ExceptionType: ex.GetType().Name);
        }
        step++;
        break;

    case AgentDecision.Follow follow:
        if (visited.Contains(follow.Url))
        {
            lastOutcome = new AgentDecisionOutcome.Failed(
                Reason: $"already-visited URL '{follow.Url}' — engine rejected",
                ExceptionType: null);
        }
        else
        {
            currentUrl = follow.Url;
            // The actual load + status capture happens at the top of the
            // next iteration; the engine stashes the load result there
            // into lastOutcome as Followed(...) before building the state.
        }
        step++;
        break;

    case AgentDecision.Act act:
        try
        {
            pendingActions = new[] { act.Action };
            // Resolution of SemanticAct happens in the transport; the
            // engine reads the resolved arm via a small new hook on
            // SemanticActCoordinator (see Implementation §3).
            lastOutcome = new AgentDecisionOutcome.ActDispatched(act.Action);
        }
        catch (Exception ex)
        {
            lastOutcome = new AgentDecisionOutcome.Failed(
                Reason: $"Act failed: {ex.Message}",
                ExceptionType: ex.GetType().Name);
        }
        step++;
        break;
}
```

For `Followed(ActualUrl, StatusCode)` specifically — the engine needs
the page-load result *before* building the next `AgentState`. ADR-0051's
load happens at the top of the loop. The engine threads
the `lastOutcome` across iterations: the `Follow` branch sets a
*pending* outcome marker; the next iteration's load fills in
`ActualUrl` / `StatusCode` and finalises the outcome. On load failure
the outcome becomes `Failed(...)` and the loop continues — the brain
gets to re-decide rather than the run dying.

The page-load-failure-terminates-run shape from ADR-0051 changes here:
load failures become `Failed` outcomes; the loop continues; the brain
sees the failure and decides Stop / try another URL. This **fixes**
the failure-mode #1 from §Context.

### 4. `LlmAgentBrain` system prompt update

The brain's system prompt (ADR-0051's `LlmAgentBrain`, with
ADR-0060's tool-calling shape) gains one paragraph:

```text
Your `state.LastOutcome` describes what happened when the engine
executed your previous decision:
- None       — first step, no prior outcome.
- Extracted  — the record was emitted; `record` shows what; `recordCount` is the running total.
- Followed   — the page loaded; `actualUrl` may differ from your proposed URL after redirects; `statusCode` is the HTTP status.
- ActDispatched — the action ran; `resolvedAction` shows what your intent became.
- Failed     — the decision failed; `reason` and `exceptionType` describe the failure class.
If the last decision failed, prefer a different approach.
```

The prompt grows by one paragraph; the brain's tool list (ADR-0060)
is unchanged.

### Bounded scope (v1)

- **One outcome per state** — `LastOutcome`, not `RecentOutcomes`.
  The history of outcomes is reconstructible from `History` +
  per-step persisted snapshots if a v2 caller surfaces a need.
- **`Record` on `Extracted` is the latest record only.** Bounded by
  design (one record, capped at the record's natural size). Earlier
  records remain in `Extracted` for context.
- **No exception object on `Failed`.** `ExceptionType` is the type
  name string; full exceptions stay in logs. A serialisable exception
  reference adds AOT / round-trip complexity for marginal brain
  benefit.
- **`Followed.StatusCode` is 0 for Dynamic page loads.** The browser
  transport doesn't surface a single HTTP status per page; the brain
  reads "0 means dynamic" from the system prompt.
- **`AgentRunSnapshot` migration.** Older snapshots (sans
  `LastOutcome`) deserialise with `LastOutcome = None`. The
  `AgentRunSnapshotCodec` reads the field defensively; this is the v1
  shape, not a v2 migration.

## Considered options

### Fork 1 — Closed sum vs. structured fields on `AgentState`

| Option | What | Verdict |
|---|---|---|
| (a) Closed sum `AgentDecisionOutcome` with per-arm-shaped data | One field on `AgentState` carrying the outcome's typed shape. | **Recommended.** ADR-0001 lineage; one arm per decision kind keeps fields tight; pattern-match in the brain (and the tests). |
| (b) Flat fields on `AgentState` — `LastExtractRecord`, `LastFollowStatus`, `LastActResolvedAction`, `LastFailureReason` | One field per outcome dimension, most null at any time. | Rejected. Wide, mostly-empty record; the "which of these is meaningful?" question becomes a per-field null-check at every brain site — the closed sum eliminates that by construction. |
| (c) `LastOutcomes` as a list (one per executed decision) | Cumulative; brain reads them all. | Rejected (v2 deferral). Capped, redundant with the persisted snapshot per step. v1's "last only" is the firecrawl-shaped feedback signal. |

### Fork 2 — Carry the actual record JSON on `Extracted`

| Option | What | Verdict |
|---|---|---|
| (a) Yes — the most recent record only | `Extracted(Record, RecordCount)`. The record is one `JsonObject`. | **Recommended.** Brain can spot "the record looks wrong" and re-decide the schema (ADR-0051 fork 4 — brain-chosen Schema each Extract). Bounded by the record's natural size. |
| (b) No — `Extracted(RecordCount)` only | The brain reads `state.Extracted[^1]` for the latest record. | Rejected. Indirection through a separate list whose nullability differs from "dropped record" vs. "no extract yet" makes the brain harder to write. |
| (c) Carry both the record and a one-page hash | `Extracted(Record, RecordHash, RecordCount)`. | Rejected. Speculative — the record itself is the hashable input; if a v2 wants a hash, it composes one. |

### Fork 3 — Carry HTTP status on `Followed`

| Option | What | Verdict |
|---|---|---|
| (a) Yes — `Followed(ActualUrl, StatusCode)` | Status is the cheapest signal for "did this page exist." | **Recommended.** Brain can avoid 404s explicitly. |
| (b) No — `Followed(ActualUrl)` only | Inferred from the page content. | Rejected. The brain can't tell a 404 page from a soft-404 from a real "no results" page without the status — the page renders identically. |
| (c) Carry status + headers + redirect chain | Full HTTP fact-bag. | Rejected (v2 deferral). Speculative; status is the high-leverage one signal. |

### Fork 4 — Carry the resolved `PageAction` on `ActDispatched`

| Option | What | Verdict |
|---|---|---|
| (a) Yes — `ActDispatched(ResolvedAction)` | A SemanticAct resolves to a concrete arm; the brain learns what its intent became. | **Recommended.** Brain can correlate the resolved arm with the page effect, useful for diagnosing "my intent resolved to the wrong button." |
| (b) No — `ActDispatched()` only | Just the marker. | Rejected. The brain proposes SemanticActs by *intent*; without seeing the resolution it learns nothing about what the resolver does. |

### Fork 5 — `Failed.Exception` serialisation

| Option | What | Verdict |
|---|---|---|
| (a) `ExceptionType` name only | One string field — the type name. | **Recommended.** Brain branches on failure class without needing exception object internals; satellite `IAgentRunStore` codec round-trips a plain string trivially. |
| (b) Full exception serialised | Serialise the exception via `Exception.ToString()` or similar. | Rejected. Full exceptions carry stack traces / inner exception graphs / non-portable data; the brain wants the *kind*, not the trace. |
| (c) Exception object reference | Carry the `Exception` instance. | Rejected. `Exception` isn't serialisable across the agent-run store boundary on most adapters; would break resumability. |

### Fork 6 — `None` arm vs. nullable `LastOutcome`

| Option | What | Verdict |
|---|---|---|
| (a) `None` arm of the closed sum | Sum stays closed; brain switches across six arms including `None`. | **Recommended.** No special-case null-handling; the brain's pattern-match covers every case. |
| (b) `AgentDecisionOutcome? LastOutcome` nullable | First step's outcome is `null`. | Rejected. Nullable + closed sum is two ways to say "missing"; the `None` arm is one. ADR-0001 closed-sum discipline. |

### Fork 7 — Outcome ordering and atomicity with the decision in `History`

| Option | What | Verdict |
|---|---|---|
| (a) `LastOutcome` refers to `History[^1]` (the last decision); engine maintains atomicity | The engine writes the snapshot with both `History` (including the new decision) and `LastOutcome` (the *previous* decision's outcome) — they describe sibling steps, not the same step. | **Recommended.** The brain reads "I just decided X (last in History); the previous step's outcome was Y" — the two are causally related but not the same step. Clean separation. |
| (b) `LastOutcome` describes the most-recent-emitted decision's outcome (same step) | Engine computes outcome *before* persisting; the persisted snapshot's `LastOutcome` is the outcome of `History[^1]`. | Rejected. Violates the persist-before-execute semantics of ADR-0051 §Decision §6 — the engine would need to *execute first, persist second* on Extract / Follow / Act, defeating the resumability guarantee. |
| (c) `LastOutcome` is a tuple `(stepIndex, outcome)` so callers know which step it describes | Explicit pairing. | Rejected. The pairing is structurally "the second-to-last History entry" by construction; making it a tuple is signal noise. |

### Fork 8 — Outcome populated on a load failure (currently terminal in ADR-0051)

| Option | What | Verdict |
|---|---|---|
| (a) Load failures become `Failed` outcomes; loop continues; brain re-decides | The brain gets to recover; the run doesn't die on one 404. | **Recommended.** Closes the failure-mode #1 from §Context. The terminal-on-load-failure shape from ADR-0051 was a hole; the outcome surface naturally fills it. |
| (b) Load failures stay terminal | Status quo. | Rejected. The whole point of this ADR is the brain-feedback gap; load failures are the most common gap. |

## Consequences

- **The brain's feedback loop closes.** Where ADR-0051 left the brain
  staring at past *decisions* with no per-step *outcome*, this ADR
  surfaces what happened. Cost-runaway from re-deciding-the-same-thing
  becomes the brain's responsibility to avoid, not a hole in the
  state shape.
- **Load failures stop terminating the run.** The `Followed.StatusCode`
  signal lets the brain skip 404s; transient page-load failures
  surface as `Failed` outcomes the brain reads next step. This is a
  behaviour change for ADR-0051's load-failure path — the run no
  longer terminates on the first failed page load.
- **`AgentRunSnapshot` shape grows one field.** Persisted snapshots
  carry `LastOutcome`. Backward-compatible read: older snapshots
  deserialise with `LastOutcome = None`.
- **Resumable runs see the previous outcome immediately.** On resume,
  the loaded snapshot's `LastOutcome` is the brain's first input —
  the run picks up causally.
- **Composes with ADR-0062.** A failed schema-validator verdict on an
  `Extract` becomes `Failed("validation: <reason>", ...)`. The brain
  learns the specific validation failure and can revise the schema
  next step.
- **Composes with ADR-0050.** `ActDispatched.ResolvedAction` is the
  concrete arm `SemanticActCoordinator` cached — the brain sees its
  intent's resolution.
- **CONTEXT.md** gains an **Agent decision outcome** term, alongside
  **Agent decision**. Relationship line linking outcome ↔ brain ↔
  validator.
- **CLAUDE.md** gets a gotcha — `AgentState.LastOutcome` is populated
  by the engine; first-step brains see `AgentDecisionOutcome.None`;
  custom brains should pattern-match the closed sum.

## Bounded scope (v1)

- **`LastOutcomes` history list** — single outcome in v1.
- **Full exception object on `Failed`** — type name only in v1.
- **Multi-status-code Follow** (e.g. all redirects in the chain) —
  one status in v1.
- **Custom outcome arms** — sum is closed (ADR-0001); consumer-
  authored arms are a v2 deferral.
- **Outcome-driven retry policy** (engine re-runs decisions whose
  outcome was Failed) — not in v1; brain decides whether to retry.

## Implementation (slice, when accepted)

**Core domain:**

1. **`WebReaper/Domain/Agent/AgentDecisionOutcome.cs`** — new closed-
   sum record with six arms.
2. **`WebReaper/Domain/Agent/AgentState.cs`** — add `AgentDecisionOutcome
   LastOutcome` property (with default `new None()`).
3. **`WebReaper/Domain/Agent/AgentRunSnapshot.cs`** — add the same
   property.

**Core engine:**

4. **`WebReaper/Core/Agent/Concrete/AgentEngine.cs`** — thread
   `lastOutcome` across iterations; populate per-arm in the switch.
   Change load-failure path from terminal to `Failed`-outcome-continue.
   Build the `AgentState` with `LastOutcome = lastOutcome`. On `Done`
   write `Stopped(reason)` into the final snapshot.

**Codec:**

5. **`WebReaper/Serialization/Agent/AgentRunSnapshotCodec.cs`** (the
   ADR-0051-shipped codec) — read / write the new field defensively
   (omit on null; default to `None` on absent input).

**LLM satellite:**

6. **`WebReaper.AI/LlmAgentBrain.cs`** — system prompt extended with
   the `LastOutcome` paragraph. (Tool-calling shape from ADR-0060 is
   unchanged.)

**Tests:**

7. **`WebReaper.Tests/WebReaper.UnitTests/AgentDecisionOutcomeTests.cs`**
   — pin each arm's shape (record equality, members).
8. **`WebReaper.Tests/WebReaper.UnitTests/AgentEngineDriverTests.cs`**
   (existing) — extend with: first-step sees `None`; Extract outcome
   round-trips the Record + count; Follow outcome reports actual URL
   + status; Act outcome reports the resolved arm; Failed outcomes
   on load failure / extraction exception / dispatch exception; the
   loop continues across a single load failure.
9. **`WebReaper.Tests/WebReaper.UnitTests/AgentRunStoreContractTests.cs`**
   (existing) — extend with: snapshot round-trip preserves
   `LastOutcome`; older snapshot without the field reads as `None`.
10. **`WebReaper.Tests/WebReaper.AI.Tests/LlmAgentBrainTests.cs`** —
    stub `IChatClient` asserts the system prompt now includes the
    `LastOutcome` paragraph; integration test demonstrating the brain
    can read `LastOutcome` (the state is delivered as a tool-call
    argument shape post-ADR-0060).

**Docs:**

11. **CONTEXT.md** — new **Agent decision outcome** term; relationships
    line linking outcome → brain → validator (ADR-0062).
12. **CLAUDE.md** — gotcha on `LastOutcome`'s `None` first-step value
    and the closed-sum shape.

### Guardrails (when implemented)

- `dotnet build WebReaper.sln` — 0 errors.
- `dotnet test WebReaper.Tests/WebReaper.UnitTests` — all pass; new
  outcome / engine tests pass.
- `dotnet test WebReaper.Tests/WebReaper.AI.Tests` — all pass; brain
  prompt change verified.
- `WebReaper.AotSmokeTest` — unchanged (closed sum + record fields
  are AOT-safe).

## References

- ADR-0001 — closed-sum pattern; the discipline `AgentDecisionOutcome`
  follows.
- ADR-0022 — Crawl driver + outstanding-work latch; the *other*
  driver, whose page-load-failure path the agent driver now
  *diverges* from (agent recovers; crawl doesn't, because the
  crawl's Job model is different).
- ADR-0050 — semantic page actions; `ActDispatched.ResolvedAction`
  surfaces the resolver's cached arm.
- ADR-0051 — agent crawl driver; this ADR amends the load-failure
  behaviour from terminal to continue-with-outcome.
- ADR-0059 — `LlmCall<TResponse>`; how the brain reads `LastOutcome`
  via the tool-call argument shape (post-ADR-0060).
- ADR-0060 — tool-calling brain; the brain reads `LastOutcome` as
  part of the bounded state delivered to the tool-call descriptor.
- ADR-0062 — `ISchemaValidator` seam; a failed-validator verdict
  becomes a `Failed("validation: <reason>", ...)` outcome the brain
  sees next step. This is the *real* composition of the two ADRs.
