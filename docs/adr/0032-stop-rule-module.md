# The Crawl driver's stop rule becomes a module; the Outstanding-work latch's credit protocol collapses to one op

## Status

**Accepted — implementation complete** (2026-05-22; landed on branch
`adr-0032-stop-rule` off `origin/master`). Seventh ADR of the
`/improve-codebase-architecture` review wave (after ADR-0026 through
ADR-0031, PRs #82-88, all merged), and **candidate #1 of the
2026-05-22 review** — the deepest of the five surfaced, picked first.
Breaking: `IOutstandingWorkLatch` (a Tier-1 public seam, ADR-0023) loses
`AddAsync` and `SignalProcessedAsync` changes signature. Folds into the
10.0.0 wave the user is batching.

## Context

CONTEXT.md says the **Crawl driver** *"own[s] the Crawl-global state
(the Visited-link tracker and the stop rule)"* — "the stop rule",
singular, named as one of the driver's responsibilities. There is no
stop-rule module. The decision *"should this Crawl stop now, and why?"*
is smeared across `ScraperEngine.RunAsync` as inline conditionals, and
it is realised a **third** time, drifted, in the consumer-authored
distributed driver.

**In-process** — [ScraperEngine.cs](../../WebReaper/Core/ScraperEngine.cs),
`RunAsync` — termination is three separate things:

- the **Outstanding-work latch** (drained), but only its `SeedAsync` /
  `AddAsync` / `SignalProcessedAsync` calls are each guarded by
  `if (config.StopWhenDrained)` — the seam ADR-0022 built as *the*
  completion mechanism is inert in a default crawl (`StopWhenDrained`
  defaults to `false`);
- the soft **page limit** — no seam, an inline
  `GetVisitedLinksCount() >= config.PageCrawlLimit` check written
  **twice** (the pre-crawl gate and the post-`Parsed` gate);
- the empty-start-URLs short-circuit.

`Scheduler.Complete()` is called from **four** sites. The latch's
credit-conservation ordering (`AddAsync` strictly before
`SignalProcessedAsync` for the parent) lives only in an XML-doc comment
on `IOutstandingWorkLatch` and a `// Credit children BEFORE …` comment
at the call site — reorder the two lines and termination silently
unbalances, with nothing failing.

**Distributed** — [WebReaperSpider.cs](../../Examples/WebReaper.AzureFuncs/WebReaperSpider.cs),
the consumer-authored Crawl-driver adapter (ADR-0009 DIY pattern) —
hand-rolls the latch protocol with its own copy of the
`// Credit children BEFORE …` comment, **and has no page-limit gate at
all**. ADR-0022's prose (*"the distributed driver reads the limit gate
and acks-without-enqueuing"*) describes a behaviour the adapter never
got: the soft limit silently did not survive the copy. The seeding
endpoint, [StartScraping.cs](../../Examples/WebReaper.AzureFuncs/StartScraping.cs),
hand-rolls `SeedAsync` with a third copy of the credit comment.

Measured against
[LANGUAGE.md](../../.claude/skills/improve-codebase-architecture/LANGUAGE.md):

**The deletion test on "the stop rule".** There is nothing to delete —
it is not a module, it is scattered statements. The latch is a
*partial* seam (one of three stop conditions, and flag-gated off by
default); the page limit has no seam; the verdict *"is the Crawl over?"*
has no home. The one genuinely shared piece is `IOutstandingWorkLatch`,
and even its **protocol** is re-hand-rolled — as a comment — in all
three places. This is the ADR-0002/0003/0006 *"copies drifted into a
bug that exists in only one copy"* shape, here in the termination
cluster: the drifted copy is the distributed driver, and the bug it
drifted into is the missing page limit.

The friction has one root: **the stop rule is a concept the domain
language names but no module realises.** ADR-0022 named the role and
built the latch seam; it never gave the verdict itself a home, and left
the page limit inline.

## Decision

**Two concepts, not one.** Completion and cutoff are different in kind
and stay separate mechanisms:

- **Completion** — the Crawl genuinely exhausted its discovered work.
  Exact, consensus-requiring, count-to-zero, CAS-fenced single trip.
  This is the **Outstanding-work latch**, already a deep, correct,
  two-adapter seam. Untouched as a mechanism.
- **Cutoff** — a soft ceiling: *stop early, we have done enough.*
  Threshold against a running total, overshoot-tolerant by design
  (ADR-0022 rejected an exact limit). Today there is exactly one — the
  **page limit**.

The deepening is **not** to merge them. It is to give the *verdict that
composes them* a home.

### 1. The stop rule becomes a module

A new internal `StopRule`
([WebReaper/Core/Crawling/Concrete/StopRule.cs](../../WebReaper/Core/Crawling/Concrete/StopRule.cs))
is the one home for *"should this Crawl stop, and why?"*. It composes
the latch (completion) and the page limit (cutoff) behind three
operations the in-process Crawl driver consults:

```csharp
internal sealed class StopRule
{
    StopRule(IOutstandingWorkLatch latch, IVisitedLinkTracker linkTracker,
             int pageCrawlLimit, bool stopWhenDrained, ILogger logger);

    // Seed the latch (under StopWhenDrained) and detect a Crawl that is
    // over before it starts — no start work, or a resumed visited count
    // already at the page limit.
    Task SeedAsync(int startJobCount);

    // Has the Crawl already concluded? The pre-crawl gate — a lock-free
    // flag read, no per-Job visited-count round-trip.
    bool IsCrawlOver { get; }

    // Register one processed Job (it discovered childCount children).
    // Returns this Job's latch credit and credits its children in one
    // atomic step, checks the page limit, and returns true to exactly
    // one caller — the Job whose registration concluded the Crawl.
    Task<bool> RegisterProcessedAsync(int childCount);
}
```

The driver loop reduces to *skip-if-over → crawl → enqueue → register*.
Every termination conditional, the doubled limit check, the empty-start
case, and the four `Scheduler.Complete()` sites collapse into it
(`Complete()` is now called from two sites, each a one-line act on the
stop rule's verdict). `StopRule` has no `IScheduler` dependency: it
*reports* the verdict; the driver *acts* — the ADR-0001/0022 posture
(*"the shell reports, the driver decides"*) applied to termination.

`StopRule` is **Tier-2 internal** (ADR-0023): reached only by
`ScraperEngine`, named by nobody public. `ScraperEngine`'s public
constructor is **unchanged** — `StopRule` is built inside `RunAsync`
from the config (which carries the limit and `StopWhenDrained`) once it
is read.

### 2. The latch's credit protocol collapses to one atomic op

`IOutstandingWorkLatch` loses `AddAsync`; `SignalProcessedAsync` takes
the child count:

```csharp
public interface IOutstandingWorkLatch
{
    Task SeedAsync(int startJobCount);
    Task<bool> SignalProcessedAsync(int childCount);  // was: AddAsync + SignalProcessedAsync()
}
```

`SignalProcessedAsync(childCount)` means *"this unit of work is done; it
discovered `childCount` children — credit them and return this unit, as
one step; return true iff outstanding credit reached zero."* The latch's
**two-call protocol is gone**: there is no longer an `AddAsync`-then-
`SignalProcessedAsync` ordering for a caller to get wrong, and no
`// Credit children BEFORE …` comment to re-hand-roll across drivers —
the latch *interface* now carries zero ordering obligations. A driver
still places that one call before it enqueues the discovered children
(children must be credited before they can be dequeued — intrinsic to
credit accounting), but that is one latch call sequenced before one
scheduler call in a single method body, not a two-method latch contract
drifting across three copies. Net: three calls with two ordering
constraints become two calls with one.

- `InMemoryOutstandingWorkLatch` — one `Interlocked.Add(ref _pending,
  childCount - 1)`, trip iff the result is zero.
- `RedisOutstandingWorkLatch` — one `StringIncrement(counter,
  childCount - 1)` (down from `INCRBY` + `DECRBY` — **one round-trip,
  not two**), then the existing SET-NX completion fence.

This is the genuine cross-driver win and the reason candidate #1's
distributed angle does **not** need a shared stop-rule module: the
latch is the shared primitive, and a primitive whose *use* is
structurally required cannot drift the way an optional shared module a
consumer must remember to call can.

### 3. No cutoff family

The page limit stays the single cutoff. A *family* — wall-clock budget,
error-rate cap, data-volume cap — is **not** built: the maintainer is
not sure one is needed, and one adapter is a hypothetical seam
(LANGUAGE.md; the ADR-0026 discipline). `StopRule` houses the limit so a
second cutoff is a *local* change to one module, but it is not
seam-ed speculatively.

### Bounded scope — what this does NOT change

- **The latch's correctness model.** Credit conservation, exact
  count-to-zero, the CAS-fenced one-shot — all unchanged. Only the
  *interface shape* tightens (two methods become one); ADR-0022's
  mechanism stands.
- **`Scheduler.Complete()` as the in-process termination mechanism.**
  The stop rule centralises the *verdict*; the driver still effects it
  with `Scheduler.Complete()`. `Complete()` being a no-op for durable
  schedulers (so `StopWhenAllLinksProcessed()` + a Redis scheduler does
  nothing) is a **documented** limitation (`ConfigBuilder`:
  *"applies to the in-memory scheduler"*), not a bug — see Considered
  option (d).
- **Observable engine behaviour.** The crawl-limit and stop-when-drained
  outcomes are byte-identical; `ScraperEngineDriverTests` and
  `EngineStopWhenDrainedTests` pass unchanged. This is a relocation, not
  a behaviour change.
- **The distributed driver stays consumer-authored** (ADR-0009).
  `WebReaperSpider` is updated for the collapsed latch op only; no
  shared stop-rule module is shipped.
- **ADR-0022's `CrawlOutcome` / `JobReport` / Spider reduction.**
  Untouched — this ADR works one layer up, in the driver loop.

## Considered options

### (a) Merge the page limit into the latch — one detector, several trip conditions

Make the limit another way the latch concludes. **Rejected:** it either
corrupts the latch's single clean invariant (credit conserved →
count-to-zero) or bolts an unrelated mechanism onto it and calls the
pair one thing — the *"second, unrelated completion mechanism"* smell
ADR-0022 rejected option β for. Completion is exact and
consensus-requiring; a cutoff is soft and threshold-based. Two concepts.

### (b) A shared stop-rule module both drivers consume

Ship `StopRule` in core; have the consumer's `WebReaperSpider` call it
too. **Rejected:** ADR-0009 makes the distributed driver
consumer-authored boilerplate. A shared module the consumer must
*remember to call* does not prevent drift — forgetting to call it is the
same drift that already lost the page limit. The thing that *cannot* be
forgotten is a primitive whose use is structurally required — the latch.
So the anti-drift investment goes there (move 2), not into a coordinator
the consumer can skip.

### (c) An `IStopRule` seam

Give the stop rule an interface. **Rejected:** one adapter is a
hypothetical seam (LANGUAGE.md). `StopRule` has exactly one
implementation and one caller (`ScraperEngine`). It is an internal
module, not a seam — depth here is *locality* (one home for the
verdict), which a single concrete class delivers.

### (d) Make "stop" cease the driver's consumption instead of `Scheduler.Complete()`

Have the verdict cancel the `GetAllAsync` enumeration, so termination
works for every scheduler (today `Complete()` is a no-op for durable
ones). **Considered — deferred, not rejected.** It is a real
improvement, but it changes termination *semantics* (a graceful
channel-drain becomes an abrupt cancel) and *behaviour* (durable
scheduler + `StopWhenDrained` goes from runs-forever to returns), and
the no-op is a *documented* limitation, not a bug. Bundling it would
break this ADR's "bounded scope" discipline. Named here as a distinct
follow-up candidate so a future review does not assume it absent.

### (e) Build the cutoff family now

A seam with page-limit / time-budget / error-rate / volume adapters.
**Rejected:** speculative — the maintainer is unsure a second cutoff is
needed, and one adapter is a hypothetical seam. `StopRule` houses the
limit so a second is a local add; the seam waits for a real second
adapter.

### (f) Keep `AddAsync` + `SignalProcessedAsync` separate; only centralise the call sites

Introduce `StopRule` but leave the latch interface alone. **Rejected:**
it leaves the credit-ordering footgun (comment-enforced, hand-rolled in
every driver including the consumer's) in place. The collapse is what
makes misuse *unrepresentable* — the ADR-0028/0030 "enforce at the
construction/call site" lineage, applied to the latch protocol — and it
is the only part of this change that reaches the consumer-authored
distributed driver.

## Consequences

- **The stop rule has one home.** *"Is the Crawl over, and why?"* is a
  module (`StopRule`), not statements smeared across a 120-line method.
  Adding a future cutoff is a local change there; the four
  `Scheduler.Complete()` sites are two, each a verdict-act.
- **The latch's two-call ordering footgun is gone.** One atomic
  `SignalProcessedAsync(childCount)` replaces the ordered `AddAsync` +
  `SignalProcessedAsync()` pair — in core *and* in the consumer-authored
  `WebReaperSpider` — so the latch interface has no ordering to
  re-hand-roll. What remains is the intrinsic register-before-enqueue
  sequencing (one latch call before one scheduler call); the old
  `// Credit children BEFORE this Job's unit is returned` comments give
  way to a single `// register … BEFORE enqueueing` note per driver.
- **The Redis latch does one round-trip, not two** — a single
  `StringIncrement` of `childCount - 1` replaces `INCRBY` then `DECRBY`.
- **An unbounded crawl does zero visited-count round-trips for the
  limit.** The pre-crawl gate is a lock-free flag read; the limit check
  short-circuits on `int.MaxValue`. Previously every Job did a
  `GetVisitedLinksCount()` (a Redis round-trip with a durable tracker)
  for the inline limit gate.
- **The verdict is testable offline.** New `StopRuleTests` exercise
  conclude-on-limit, conclude-on-drained, the limit-wins-when-smaller
  composition, and never-conclude — without a running `ScraperEngine`.
  The candidate's payoff: the stop rule's interface *is* the test
  surface.
- **`IOutstandingWorkLatch` is a breaking change** — `AddAsync` removed,
  `SignalProcessedAsync` re-signatured. It is a Tier-1 public seam
  (ADR-0023): `RedisOutstandingWorkLatch` and the `WebReaperSpider`
  consumer are updated in this change; a custom latch adapter must
  adopt the new shape. SemVer **major** — folds into the unreleased
  10.0.0 wave.
- **The distributed driver's missing page limit is named, not fixed
  here.** `WebReaperSpider` still has no cutoff; closing that (and
  ADR-0022's overclaiming prose) is a distributed-driver concern,
  separate from this in-process deepening.
- **CONTEXT.md** gains **Stop rule** as a defined term; the
  **Outstanding-work latch** entry is updated for the collapsed op; a
  new "Flagged ambiguities" bullet records the decision and the
  deferred-cutoff-mechanism option.

## Implementation

Landed on `adr-0032-stop-rule`:

1. **`WebReaper/Core/Crawling/Abstract/IOutstandingWorkLatch.cs`** —
   `AddAsync` removed; `SignalProcessedAsync` takes `int childCount`;
   XML docs state the credit-conservation ordering is now structural.
2. **`WebReaper/Core/Crawling/Concrete/InMemoryOutstandingWorkLatch.cs`**
   — one `Interlocked.Add(ref _pending, childCount - 1)`.
3. **`WebReaper.Redis/RedisOutstandingWorkLatch.cs`** — one
   `StringIncrement(counter, childCount - 1)` then the SET-NX fence.
4. **`WebReaper/Core/Crawling/Concrete/StopRule.cs`** (new) — the
   in-process stop-rule module.
5. **`WebReaper/Core/ScraperEngine.cs`** — `RunAsync` builds a
   `StopRule` and reduces to skip-if-over → crawl → enqueue → register;
   the inline latch/limit/empty-start logic and two of the four
   `Complete()` sites are gone.
6. **`Examples/WebReaper.AzureFuncs/WebReaperSpider.cs`** — the
   `AddAsync` + `SignalProcessedAsync()` pair becomes one
   `SignalProcessedAsync(children.Length)`; the hand-rolled credit
   comment is trimmed. `StartScraping.cs` is unchanged (`SeedAsync`
   only).
7. **Tests** — `OutstandingWorkLatchTests` rewritten to the collapsed
   op; new `StopRuleTests`; `RedisDistributedAdapterTests` updated for
   the new latch shape.
8. **`CONTEXT.md`** — **Stop rule** term added; **Outstanding-work
   latch** entry updated; one new "Flagged ambiguities" bullet.

### Guardrails (run on the branch tip)

- `dotnet build WebReaper.sln` — **0 errors**. The 19 warnings are all
  pre-existing (CS8618 nullable-field, CS8633 logger-generic-variance,
  CS1574, xUnit1031, NU1510); none references a file ADR-0032 touches,
  and `WarningsAsErrors=CS1591` on core stays green — `StopRule` is
  Tier-2 internal, and the changed `IOutstandingWorkLatch` members carry
  their XML docs.
- `dotnet test WebReaper.Tests/WebReaper.UnitTests` — **161/161 pass**
  (153 pre-0032 + 1 new `OutstandingWorkLatchTests` case + 7 new
  `StopRuleTests`). `OutstandingWorkLatchTests` is rewritten to the
  collapsed op; `ScraperEngineDriverTests` and `EngineStopWhenDrainedTests`
  pass **unchanged** — the relocation is behaviour-preserving.
- `dotnet test WebReaper.Tests/WebReaper.Redis.Tests` — **8/8 pass**
  (the satellite whose `RedisOutstandingWorkLatch` adopted the collapsed
  op).
- The satellite + Examples projects compile in the whole-solution build
  (`WebReaper.AzureFuncs`'s distributed driver updated to the collapsed
  op); the live-site `WebReaper.IntegrationTests`
  (`RedisDistributedAdapterTests`, updated) run on CI. AOT is
  unaffected — the change adds no reflection, serialization, or dynamic
  codegen.

## References

- ADR-0022 — the Crawl driver, the Outstanding-work latch, the soft
  page limit; this ADR finishes its "stop rule" by giving it a module.
- ADR-0009 — the distributed driver is consumer-authored; why move 1 is
  in-process and move 2 (the shared latch primitive) carries the
  cross-driver win.
- ADR-0028 / ADR-0030 — the "enforce the invariant at the
  construction/call site so misuse is unrepresentable" lineage the
  latch-protocol collapse continues.
- ADR-0023 — the Tier-1 / Tier-2 split: `StopRule` is Tier-2 internal,
  `IOutstandingWorkLatch` is a Tier-1 public seam.
