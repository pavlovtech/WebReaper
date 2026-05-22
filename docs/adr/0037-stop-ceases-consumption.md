# Stop ceases the Crawl driver's consumption; `IScheduler.Complete()` is removed

## Status

**Accepted — implementation complete** (2026-05-22; landed on branch
`adr-0037-stop-ceases-consumption` off `origin/master`). A fresh
`/improve-codebase-architecture` pass after the 2026-05-22 wave (ADR-0032
through ADR-0036, all merged) — the maintainer's final architecture pass
before the AI-native work — surfaced three candidates; this is **candidate
#1, picked first.** It **closes ADR-0032's deferred option (d).** Breaking:
`IScheduler` (a Tier-1 public seam, ADR-0023) loses `Complete()`. Folds into
the unreleased 10.0.0 wave.

## Context

CONTEXT.md's **Stop rule** entry: the driver *"reports the verdict and the
driver acts (`Scheduler.Complete()`)."* `Scheduler.Complete()` is the
in-process **Crawl driver**'s one lever for ending a **Crawl** — and it is a
leaky seam.

`IScheduler.Complete()` ships as a **default no-op** on the interface
(`void Complete() { }`). The `GetAllAsync` doc admits the consequence: the
stream *"ends only once `Complete` has been signalled (the in-memory
adapter) — a durable / distributed adapter keeps streaming by default."*

`ScraperEngine.RunAsync` drives `Parallel.ForEachAsync` over
`Scheduler.GetAllAsync()`; the loop ends only when that async stream ends.
Two sites call `Scheduler.Complete()` — the pre-loop gate, and the per-Job
**Stop rule** `concluded` verdict. **Only `InMemoryScheduler` overrides
`Complete()`** (`_jobChannel.Writer.TryComplete()`). The four durable
adapters — `FileScheduler` (a **Core adapter**, the zero-dependency durable
default), `RedisScheduler`, `SqliteScheduler`, `AzureServiceBusScheduler` —
inherit the no-op. So with any **Scheduler** but in-memory, `RunAsync`
**never returns**: the loop body sees `stopRule.IsCrawlOver` and `return`s —
skipping the work — but `GetAllAsync()` keeps yielding (or polling) forever.
The driver checks the verdict *inside* the loop; it has no way to stop
*entering* it.

Termination is modelled as a **producer-side signal**: *ask the Scheduler to
stop producing.* But a durable, possibly-shared **Scheduler** genuinely
*cannot* stop producing on command — it cannot know another worker will not
enqueue a **Job**. `Complete()` is meaningful only for the in-memory
channel; for everything else it is structurally a no-op, not laziness. A
consumer who swaps the in-memory **Scheduler** for `FileScheduler` to get a
resumable crawl silently loses termination — no error, the process simply
never finishes.

**The reframe — the seam already has a universal stop mechanism.** Every
`IScheduler.GetAllAsync(ct)` adapter already honours its `CancellationToken`:
`InMemoryScheduler` (`ReadAllAsync(ct)`), `RedisScheduler` /
`SqliteScheduler` (`while (!ct.IsCancellationRequested)`, the empty-wait is
`Task.Delay(300, ct)`), `FileScheduler` (`while (true)`, but every await
inside takes `ct`), `AzureServiceBusScheduler` (`ReceiveMessagesAsync(ct)`).
Cancelling the `GetAllAsync` token ends the stream for **every** adapter,
uniformly. `Complete()` is therefore not merely leaky — it is a *second,
partial* mechanism layered beside a universal one.

Measured against
[LANGUAGE.md](../../.claude/skills/improve-codebase-architecture/LANGUAGE.md):
the **deletion test** on `Complete()` — delete it, and the in-process
driver's termination must relocate (the driver had to *do something*) — but
it relocates *into the driver* as a token cancel, not across N callers; the
durable adapters lose nothing (they already ignored it). `Complete()` is a
shallow, leaky member: a Tier-1 interface obligation whose contract holds
for one adapter of five. This is exactly ADR-0032's deferred option (d).

## Decision

**Termination is a consumer-side decision, not a producer-side signal.** The
**Crawl driver** ends a **Crawl** by ceasing its *own* consumption of the
job stream; it never asks the **Scheduler** to stop producing.

### 1. `IScheduler.Complete()` is removed

The universal mechanism is the `GetAllAsync` `CancellationToken`. `Complete()`
is deleted from `IScheduler`; `InMemoryScheduler` drops its override (its
`GetAllAsync` is `ReadAllAsync(ct)`, which already ends on token-cancel — the
channel is simply never completed now). `IScheduler` is left with
`DataCleanupOnStart`, `AddAsync` ×2, `GetAllAsync` — every member a real,
uniformly-honoured contract.

The `GetAllAsync` contract is **sharpened**: the stream ends when its
`CancellationToken` is cancelled (or, for a finite source, when the source is
exhausted); every adapter must observe the token *promptly* — a wait for the
next **Job** (a poll delay, a blocking receive) must be cancellable. This was
already true of all five adapters; the contract now *states* it instead of
leaving *"durable adapters keep streaming by default"* as the documented
shape.

### 2. The driver owns a "Crawl concluded" cancellation source

`ScraperEngine.RunAsync` creates a `CancellationTokenSource` linked to the
caller's token and drives both `Scheduler.GetAllAsync(...)` and
`Parallel.ForEachAsync` on its token. The two former `Scheduler.Complete()`
sites become:

- **the pre-loop gate** — if the **Stop rule** concluded before the loop
  began (no start work to drain, or a resumed visited count already at the
  page limit), `RunAsync` simply `return`s without driving the loop;
- **the per-Job `concluded` verdict** — the **Job** whose
  `RegisterProcessedAsync` concluded the **Crawl** calls `crawlCts.Cancel()`.
  `GetAllAsync` stops yielding and the parallel loop unwinds.

`RunAsync` catches the resulting `OperationCanceledException`: if `crawlCts`
is cancelled but the caller's token is **not**, it was the driver's own
conclude-cancel — the normal, successful end of a finished **Crawl**:
swallow, log, return. If the caller's token *is* cancelled, rethrow — caller
cancellation of `RunAsync` is unchanged.

### 3. In-flight Jobs abort on a cutoff stop

The `Parallel.ForEachAsync` body runs every per-Job operation — the
retry-wrapped `Spider.CrawlAsync`, `ProcessTargetPage`, the child `AddAsync` —
on its own iteration token (linked to `crawlCts`). When the **Stop rule**
concludes, in-flight **Job**s are **aborted** (their token cancels), not
drained.

This is observable only for a **cutoff** (the soft page limit): on
**completion** (the **Outstanding-work latch** drained) nothing is in flight
by definition — the last **Job** is the one that observed `concluded`.
ADR-0022 documented the limit as soft with *"in-flight iterations still
finish"*; this ADR changes that to *in-flight iterations abort*. The limit is
explicitly soft and overshoot-tolerant (ADR-0022 rejected an exact limit);
whether the last ≈`ParallelismDegree` pages finish or abort is a trivial
difference, and aborting hews tighter to the cap. A **Retry policy** never
retries `OperationCanceledException` (ADR-0026), so an aborted `Spider` call
propagates its cancellation once, with no retry amplification.

### The `OperationCanceledException` and "termination is a value"

ADR-0022 / CLAUDE.md state *"termination is a value, never an exception."*
That stands. The **verdict** — *is the Crawl over, and why?* — is still a
value: the **Stop rule** reports a `bool`. The `OperationCanceledException`
here is not a termination *signal*: it is the cooperative-cancellation
unwind of the driver's *own* `Parallel.ForEachAsync`, caught in the same
method that raised it, never crossing a public seam, never observed by the
`RunAsync` caller (who sees a normal return on a completed **Crawl**), never
retry-amplified (`RetryPolicy` excludes OCE — ADR-0026). The exception
ADR-0022 removed was `PageCrawlLimitException` thrown *from the Spider shell,
across the shell→driver seam,* as retry-amplified control flow. This OCE is
the opposite: an internal loop-unwind. "Termination is a value" is a
statement about seams — and no seam carries an exception.

### Bounded scope — what this does NOT change

- **The Stop rule's verdict logic** — completion (the latch) and cutoff (the
  page limit), composed by `StopRule` (ADR-0032). Unchanged. This ADR changes
  only how the driver *acts* on the verdict.
- **The Outstanding-work latch** — untouched.
- **The distributed driver stays consumer-authored** (ADR-0009). It never
  called `Scheduler.Complete()` — it consults the **Outstanding-work latch**
  directly (ADR-0032) — so removing `Complete()` does not touch it. When a
  per-message distributed worker stops is its own concern, as ADR-0032
  scoped.
- **Caller cancellation of `RunAsync`** — unchanged: a caller-cancelled token
  still surfaces as a thrown `OperationCanceledException`.
- **Observable engine behaviour for the in-memory Scheduler** — a drained or
  limit-capped crawl still returns from `RunAsync` normally;
  `ScraperEngineDriverTests` and `EngineStopWhenDrainedTests` pass unchanged.
  What changes is that the *same* now holds for every durable **Scheduler**.

## Considered options

### (a) Keep `Complete()`, make it a required (non-default) member

Promote `void Complete() { }` to a member every adapter must implement, and
have each durable adapter wire it (a flag its `GetAllAsync` loop checks).
**Rejected:** a durable, *shared* **Scheduler** (Redis, Azure Service Bus)
genuinely cannot honour *"no more Jobs will ever be added"* — another worker
may add one; an honest implementation is impossible, so the member would be
a forced lie or a no-op by another name. And it keeps two termination
mechanisms (the token *and* `Complete()`) where the token alone is
universal. Making a structurally-wrong obligation mandatory is worse than
removing it.

### (b) Keep `Complete()` as-is; document the limitation harder

The status quo — `Complete()` works for in-memory, and `ConfigBuilder`'s
`StopWhenAllLinksProcessed` doc says it *"applies to the in-memory
scheduler."* **Rejected:** this *is* the leaky seam the candidate is about. A
documented footgun is still a footgun; *"swap in `FileScheduler` and the
crawl never ends"* is a silent correctness failure no doc prevents.

### (c) Wrap `GetAllAsync` in a driver-side enumerable that `yield break`s on the verdict — no cancellation

Have the driver wrap the stream and stop yielding once `StopRule.IsCrawlOver`.
**Rejected:** it deadlocks the **completion** case. When the queue drains,
the wrapper is parked *inside* the underlying `GetAllAsync().MoveNextAsync()`
waiting for the next **Job** — for the in-memory channel, a `ReadAllAsync`
wait that blocks indefinitely. The wrapper never regains control to check the
verdict and `yield break`. Unblocking a parked `MoveNextAsync` from the
consumer side requires either the producer completing (that *is*
`Complete()`) or the token cancelling. Cancellation is unavoidable; the
wrapper adds nothing.

### (d) Preserve "in-flight iterations finish" on a cutoff via two tokens

Drive `GetAllAsync` on the conclude token but the **Job** bodies on the
caller's token, so a cutoff ends enumeration without aborting in-flight
**Job**s. **Rejected:** it leans on `Parallel.ForEachAsync`'s behaviour when
the *source enumerator* faults vs. when in-flight bodies are mid-flight —
fiddlier, and a property worth a spike to trust. The limit is soft (ADR-0022
rejected an exact limit); aborting the last ≈`ParallelismDegree` in-flight
pages is within the overshoot tolerance and is arguably the more correct
response to a cap. One token, abort-in-flight, is simpler and honest.

### (e) Catch the conclude-cancel inside a wrapper so `RunAsync` never sees an exception

A wrapper enumerable that swallows the conclude-`OperationCanceledException`
and converts it to a clean stream end. **Rejected:** C# forbids `yield`
inside a `try`/`catch`, so the wrapper needs a hand-rolled enumerator loop —
more machinery than catching one `OperationCanceledException` at the
driver's own `try` boundary, which is the idiomatic `Parallel.ForEachAsync` +
`CancellationToken` pattern. The OCE is caught once, in the method that
raised it; that is not control-flow-by-exception across a seam.

## Consequences

- **Termination is uniform across every Scheduler.** A drained or
  limit-capped crawl returns from `RunAsync` for `FileScheduler`,
  `RedisScheduler`, `SqliteScheduler` and `AzureServiceBusScheduler` exactly
  as it already did for `InMemoryScheduler`. The *"swap in a durable
  Scheduler and the crawl hangs"* footgun is gone. `StopWhenAllLinksProcessed()`
  now applies to every **Scheduler** — its `ConfigBuilder` doc is corrected
  (it said *"applies to the in-memory scheduler"*).
- **`IScheduler` shrinks to four members, each a uniformly-honoured
  contract.** No member's contract holds for one adapter of five. The leaky
  seam is removed, not patched.
- **`IScheduler.Complete()` removal is breaking.** A Tier-1 public seam
  (ADR-0023). Only `InMemoryScheduler` implemented it and only `ScraperEngine`
  called it — both in core; no satellite, Example, or `Misc` consumer
  references it. A consumer's custom `IScheduler` adapter that *overrode*
  `Complete()` keeps a now-orphan method (harmless); one that *called* it must
  drop the call. SemVer **major** — folds into the unreleased 10.0.0 wave
  (with ADR-0032 / 0033 / 0034 / 0035 / 0036).
- **The `GetAllAsync` token contract is now explicit.** *"Ends promptly when
  its `CancellationToken` cancels"* is the stated contract a custom adapter
  must meet — previously implicit, and the place a future durable adapter
  could regress.
- **Termination is testable through any Scheduler.** A new
  `ScraperEngineDriverTests` case drives `RunAsync` over a durable-style
  poll-based **Scheduler** whose stream ends only via its token, and asserts
  the crawl terminates — a test that *could not exist* while `Complete()` was
  the mechanism (it would hang). The candidate's payoff: termination no
  longer depends on which adapter is wired.
- **In-flight Jobs abort on a cutoff** (Decision §3) — a documented change to
  ADR-0022's *"in-flight iterations still finish"*, within the soft limit's
  overshoot tolerance.
- **CONTEXT.md** — the **Stop rule** term is updated (the driver *acts* by
  ceasing its own consumption, not `Scheduler.Complete()`); a new "Flagged
  ambiguities" bullet records the decision. **CLAUDE.md**'s run-path
  paragraph is corrected.

## Implementation

Landed on `adr-0037-stop-ceases-consumption`:

1. **`WebReaper/Core/Scheduler/Abstract/IScheduler.cs`** — `Complete()`
   removed; the `GetAllAsync` XML doc rewritten to the sharpened token
   contract.
2. **`WebReaper/Core/Scheduler/Concrete/InMemoryScheduler.cs`** — the
   `Complete()` override removed; `GetAllAsync` (`ReadAllAsync(ct)`)
   unchanged — it already ended on token-cancel.
3. **`WebReaper/Core/ScraperEngine.cs`** — `RunAsync` owns a linked
   `CancellationTokenSource`; drives `GetAllAsync` and `Parallel.ForEachAsync`
   on its token; the pre-loop gate `return`s; the per-Job `concluded` verdict
   calls `crawlCts.Cancel()`; the catch distinguishes the driver's
   conclude-cancel (swallow, return) from caller cancellation (rethrow).
   Per-Job operations run on the iteration token.
4. **`WebReaper/Core/Crawling/Concrete/StopRule.cs`** — XML doc updated (the
   driver acts by ceasing consumption, not `Scheduler.Complete()`).
5. **`WebReaper/Core/LinkTracker/Abstract/IVisitedLinkTracker.cs`** — the
   `TryAddVisitedLinkAsync` doc's stale *"mirrors `IScheduler.Complete()`"*
   cross-reference reworded.
6. **`WebReaper/Builders/ConfigBuilder.cs`** — `StopWhenAllLinksProcessed`'s
   doc corrected: it applies to every **Scheduler**, not only the in-memory
   one.
7. **Tests** — `ScraperEngineDriverTests` gains a `PollingScheduler`
   durable-style fake and a termination-through-it case; the existing driver
   and `EngineStopWhenDrainedTests` cases pass unchanged.
8. **`CONTEXT.md`** — **Stop rule** term updated; one new "Flagged
   ambiguities" bullet. **`CLAUDE.md`** — run-path paragraph corrected.

### Guardrails (run on the branch tip)

- `dotnet build WebReaper.sln` — **0 errors**, 19 warnings, all pre-existing
  (CS8618 / CS8633 / CS8602 / CS8604 / CS1574 / NU1510) — none in a file this
  ADR touches; `WarningsAsErrors=CS1591` on core stays green (this ADR removes
  a public member and adds none, and the reworded `IScheduler` / `GetAllAsync`
  doc is intact).
- `dotnet test WebReaper.Tests/WebReaper.UnitTests` — **165/165 pass** (163
  pre-0037 + the two new `PollingScheduler` durable-style termination cases —
  drained and page-limit cutoff). `ScraperEngineDriverTests` and
  `EngineStopWhenDrainedTests` pass otherwise unchanged: the in-memory path is
  behaviour-preserving for the `RunAsync` caller (`InMemoryScheduler` now also
  ends on token-cancel rather than channel-completion, transparently).
- Native-AOT smoke (`WebReaper.AotSmokeTest`, `dotnet publish -r osx-arm64`) —
  **ALL PASS** (9/9). The change adds no reflection, serialization, or dynamic
  codegen — `CancellationTokenSource` / `Parallel.ForEachAsync` are AOT-clean
  BCL.
- The live-site `WebReaper.IntegrationTests` are deferred to CI (slow,
  network-flaky by design — CLAUDE.md).

## References

- **ADR-0032** — the **Stop rule** module; this ADR closes its deferred
  option (d), *"make 'stop' cease the driver's consumption instead of
  `Scheduler.Complete()`."*
- **ADR-0022** — the **Crawl driver**, the soft page limit, *"termination is
  a value"*; this ADR reconciles that with the cooperative-cancellation
  unwind.
- **ADR-0026** — the **Retry policy** never retries
  `OperationCanceledException`; an aborted in-flight **Job** is not
  retry-amplified.
- **ADR-0009** — the distributed driver is consumer-authored; this in-process
  change does not reach it.
- **ADR-0023** — the Tier-1 / Tier-2 split: `IScheduler` is a Tier-1 public
  seam, so removing `Complete()` is a SemVer-major break.
