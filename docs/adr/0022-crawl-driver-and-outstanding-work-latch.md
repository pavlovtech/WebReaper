# The Crawl driver owns termination; the per-Job shell reports a value, never throws; completion is one outstanding-work latch seam

The per-Job **Spider** shell is constructible with fakes (six interface
collaborators) yet **no test ever constructs the real `Spider`** (verified:
`grep "new Spider(" WebReaper.Tests/` — none). The reason is structural, not
laziness: `ISpider`'s entire interface is `Task<List<Job>> CrawlAsync(Job,
CancellationToken)` (`ISpider.cs:5-8`), and the three things that actually
matter escape through side channels that are **not on the interface**:

- **Emitted data / callbacks** — `ScrapedData` and `PostProcessor` are C#
  `event`s on the concrete `Spider` (`Spider.cs:99-101`), not on `ISpider`;
  **Sink** fan-out is a `List<IScraperSink>` mutated by side effect. The only
  production caller, `ScraperEngine`, holds an `ISpider` and cannot observe any
  of it.
- **Crawl termination** — a thrown `PageCrawlLimitException` (`Spider.cs:92`),
  caught two layers up in `ScraperEngine.RunAsync` (`ScraperEngine.cs:100-103`).
- **Dedup** — folded silently into the returned list (`Spider.cs:79-83`).

Between the shell and the catch sits `Executor.RetryAsync` =
`Policy.Handle<Exception>().RetryAsync(3)` (`Executor.cs:8`, wired at
`ScraperEngine.cs:81`). `Handle<Exception>()` handles **every** exception, so
the **Crawl**-terminating signal and a transient `HttpRequestException` travel
the same channel through the same fault-retry wrapper, which cannot tell them
apart: every worker that hits the limit re-enters `CrawlAsync` three pointless
times before the exception escapes. No test written against today's `ISpider`
could catch this — the signal is not on the interface; you would have to assert
on Polly's internal retry count. **The interface is not the test surface.**

The same disease was cured one layer down by ADR 0001: the **Crawl step** used
to leak its result through "two look-alike call sites" until `CrawlOutcome`
made it a closed returned value. The shell never got that treatment. The
crawl-limit-as-thrown-exception is **incidental control flow that ended up
there because there was no returned channel to carry "stop"** (maintainer,
grill).

Deletion test on the miscoupling: `IVisitedLinkTracker` is **Crawl-global**
state — "what has *the Crawl* visited" — wired as a per-Job shell collaborator.
Delete it from `Spider` and the complexity does not vanish; it reappears **in
the drivers, correctly placed**: the in-process driver can mark-and-dedup
atomically because it owns the parallel loop; the distributed driver must make
its own choice. Misplaced complexity relocated to where it is coherent — a
deepening, not a pass-through. Every defect here is a symptom of that one
miscoupling:

- the limit exception (no returned channel for "stop");
- racy dedup — children are filtered against the visited set (`Spider.cs:79-83`)
  but only *marked* visited when they themselves are crawled (`Spider.cs:60`),
  so under `Parallel.ForEachAsync` two workers discover the same child, both
  see it unvisited, both enqueue it;
- the **distributed poison message**: `Examples/WebReaper.AzureFuncs/
  WebReaperSpider.cs:46-58` does `BuildSpider()` then `CrawlAsync(job)` and
  uses only the returned `List<Job>` — no `ScraperEngine`, no limit, no retry,
  no event subscription. When the shared visited count crosses the limit, the
  shell *throws*; nothing catches it; Service Bus redelivers; every subsequent
  message poisons and the queue dead-letters. The limit-as-exception is not
  merely untested distributed — it is an active defect.

`ISpider.CrawlAsync → List<Job>` is therefore a public seam with **two
structurally different drivers** (in-process `ScraperEngine`; the stateless
Azure Function), and the role they share — seed, schedule, dedup, own
Crawl-global state, decide when the Crawl stops — is **unnamed in the domain
language**. ADR-0009 already made the bare-`ISpider` seam first-class
(`BuildSpider()`); this ADR names and deepens the thing on the *other* side of
it.

## Why this is worth a major break

Three concrete, technical payoffs, not a positioning bet:

1. **The interface becomes the test surface.** A per-Job *report* value makes
   "emitted iff a **Target page**", "stop on limit", and "which child Jobs
   survive dedup" assertable offline through one seam — the entire reason this
   candidate was chosen. The slow, network-flaky live-site `IntegrationTests`
   (fixed `Task.Delay` up to 25s) stop being the only thing that exercises
   crawl orchestration.
2. **Three defects close by construction** (see Deliberate consequences): the
   retry-amplified limit exception, the racy discovery dedup, and the
   distributed poison message. All are symptoms of one miscoupling; relocating
   the tracker to the **Crawl driver** removes the class, not the instances.
3. **Distributed clean-stop becomes correct, transport-independently.** The
   maintainer requires a distributed Crawl to stop cleanly when work runs out.
   Research (`research/distributed-crawl-termination.md`) establishes this
   cannot be emergent queue-drain (a documented non-solution: scrapy-redis,
   StormCrawler) and that the existing in-process `pending` counter is already
   the correct algorithm (a centralized unit-credit termination detector,
   Huang/Mattern). The deepening makes it a seam whose correctness derives from
   an atomic test-and-set, not from queue delivery semantics.

These are properties of the code, independent of how the library is positioned.

## Decision

- **Name the role: the Crawl driver.** It seeds start-URL Jobs, schedules
  them, owns the **visited-link tracker** and the stop rule, and detects Crawl
  termination. Two adapters: the **in-process driver** (`ScraperEngine`,
  `Parallel.ForEachAsync`) and the **distributed driver** (one Service Bus
  message per Job — `WebReaperSpider`). A genuine seam by the two-adapter test,
  not a hypothetical one.
- **The per-Job shell returns a Job report, never throws to signal control
  flow.** Apply the ADR-0001 move one layer up: `Spider.CrawlAsync` returns a
  closed value — the optional **ParsedData** to emit, the discovered child Jobs
  (unfiltered — dedup is the driver's), and the accounting facts the driver
  needs (this Job's identity, its child count). `PageCrawlLimitException` as a
  control-flow mechanism is removed.
- **The Spider is reduced to: load page → run Crawl step → return a Job
  report.** It no longer owns the visited-link tracker, the crawl-limit stop,
  **Sink** fan-out, or the `PostProcessor`/`ScrapedData` notification. Those
  move to the **Crawl driver**, which interprets the Job report: apply the
  idempotency authority, enqueue surviving children, mutate the latch, fan out
  to sinks, fire callbacks. ADR-0001's `CrawlOutcome` (the **Crawl step**'s
  result) is untouched and still sits *inside* the shell; the Job report wraps
  it plus emission/accounting facts.
- **The visited-link tracker is the single idempotency authority.** Its atomic
  test-and-set ("was this URL newly added?") gates dedup, the limit, **and**
  completion accounting. One atomic membership check, three uses — research
  ranks this the best-fit fix and it is the technique the correct frameworks
  use (NServiceBus Outbox, BullMQ sets; Celery's ungated counter is the cited
  cautionary tale).
- **Completion is one Outstanding-work latch seam (option α).** A centralized
  unit-credit counter: seed = #start URLs; per Job, register children and add
  their credit **before** decrementing the parent (the credit-conservation
  precondition — the existing `ScraperEngine.cs:87-89` comment *is* this);
  reaching zero trips the latch exactly once. **Two adapters:** in-memory
  `Interlocked` (in-process driver) and a distributed-atomic counter (Redis
  Lua / Cosmos transactional batch — reusing the ADR-0003/0005 Redis/Cosmos
  infra the distributed Crawl already requires). The counter mutation and the
  idempotency-authority test-and-set are **one atomic op**; correctness is
  therefore independent of Service Bus's at-least-once delivery.
- **The one-shot end-of-crawl action is idempotent, fired via compare-and-set.**
  `completion_fired` set-if-absent in the same atomic op that observes zero;
  the winning CAS runs an **idempotent** effect (upsert a completion record
  keyed by run-id; post-processing dedupes). A monotonic fencing token for an
  external/non-idempotent effect is **named and deferred**, not built (no such
  effect is in scope).
- **The page limit is soft/best-effort, uniform across both drivers** —
  overshoots by roughly the in-flight concurrency. This is already de-facto
  true in-process (the racy `CheckCrawlLimit`) and is the universal norm
  (Scrapy `CLOSESPIDER_*`, Crawlee `maxRequestsPerCrawl`, Heritrix, Colly —
  only Nutch's discrete rounds are exact). The break is making it *stated and
  uniform* instead of accidental in one driver and a poison bug in the other.
- **`StopWhenDrained` is subsumed by the latch.** The in-process `pending`
  counter (`ScraperEngine.cs:46-97`, issue #20) was the latch in disguise for
  one driver only; it becomes the in-memory adapter of the one seam. The
  distributed driver gets the same termination detection it never had — the
  maintainer's "stop cleanly when work runs out", delivered for both.

## Staging (tracer slices; the guardrail suite stays green at each)

1. **Job report + Spider reduced, in-process driver only.** Introduce the
   closed Job-report value; `Spider.CrawlAsync` returns it; the in-process
   `ScraperEngine` interprets it (fan-out, callbacks, enqueue, the existing
   `Interlocked` counter as the first latch adapter); `PageCrawlLimitException`
   control flow deleted. New offline `SpiderTests` and driver tests cross the
   now-real seam — the candidate's payoff lands here, no distributed work yet.
2. **Idempotency authority.** Make the visited-link tracker's mark an atomic
   test-and-set; gate dedup + limit + counter mutation on first-insert; the
   counter mutation and the test-and-set become one atomic op (in-memory
   adapter first — still offline-testable).
3. **Outstanding-work latch seam + distributed adapter.** Extract the latch
   interface; in-memory `Interlocked` adapter (extracted from step 1) and the
   distributed-atomic adapter (Redis Lua / Cosmos transactional batch) over the
   ADR-0005 pool. CAS'd idempotent one-shot completion.
4. **Distributed driver.** `WebReaperSpider` becomes a thin **Crawl driver**
   adapter over the Job report: apply the authority, enqueue surviving
   children, mutate the distributed latch, ack. The poison message is gone by
   construction.

Each slice keeps the guardrail green: the unit suite, the whole-solution build
(Examples included), `WebReaper.AotSmokeTest`. The distributed-latch adapter is
covered by the satellite test projects (`WebReaper.Redis.Tests` /
`WebReaper.Cosmos.Tests`), the in-memory latch and the Spider/driver
interpretation offline in `WebReaper.UnitTests`.

## Bounded scope — what this does NOT change

- **ADR-0001's `CrawlOutcome`.** Still a closed three-arm sum, still the
  **Crawl step**'s result, still computed *inside* the shell. The Job report
  *wraps* a `CrawlOutcome` with emission/accounting facts; it does not replace
  or extend it. The per-Job decision logic is byte-identical.
- **ADR-0002/0003/0004/0006 mechanisms.** The Schema fold, the keyed blob
  store + payload shells, the one `IPageLoader`/`IPageLoadTransport` seam, and
  the buffered file-sink drain are untouched. The distributed latch adapter
  *reuses* the ADR-0005 `RedisConnectionPool` and the ADR-0003 Cosmos infra;
  it does not modify them (it changes what is stored, not how the connection
  is pooled).
- **ADR-0009's registration seam and `BuildSpider()`.** Unchanged. The
  distributed worker still obtains a bare `ISpider` via `BuildSpider()`; this
  ADR changes what `ISpider` *returns* and adds a Crawl-driver role around it,
  not the registration surface.
- **The scheduler seam (`IScheduler`).** Unchanged. The Crawl driver still
  drives `Scheduler.GetAllAsync()`; the latch decides *when the Crawl is done*,
  not *how Jobs are queued*.
- **An exact page limit.** Explicitly rejected (Considered options). Soft,
  concurrency-bounded, uniform — a stated contract, not a TODO.
- **Distributed-completion via a durable-workflow runtime (option β).**
  Rejected (Considered options) — named here so a future review does not
  re-suggest "just use Azure Durable Functions".

## Deliberate consequences (bugs closed by construction — see CONTEXT.md "Flagged ambiguities")

- **The retry-amplified limit exception is gone.** "Stop" is a returned value
  the driver checks, not an `Exception` run through a fault-retry policy.
  `Executor`'s `Handle<Exception>()` no longer conflates "terminate the Crawl"
  with "the page failed, retry it". (`Executor`'s single-adapter retry policy
  is reviewed in the same slice; it has one caller and one hardcoded policy.)
- **The racy discovery dedup is gone.** Dedup and visited-marking are one
  atomic test-and-set owned by the driver, not a read-then-act gap split
  across `Spider.cs:79-83` and `:60` under `Parallel.ForEachAsync`.
- **The distributed poison message is gone.** The distributed driver reads the
  limit gate and acks-without-enqueuing instead of throwing into an Azure
  Function with nothing to catch it; Service Bus no longer dead-letters the
  queue at the limit boundary.
- **`StopWhenDrained` generalizes from one driver to a uniform termination
  detector.** Issue #20's in-process-only `pending` int becomes the in-memory
  adapter of the latch seam; the distributed driver gains clean stop it never
  had. Observable in-process behaviour (stop when drained) is unchanged.

## SemVer

**Major.** `ISpider.CrawlAsync`'s return type changes from `List<Job>` to the
closed Job-report value; `ScrapedData`/`PostProcessor` move off the concrete
`Spider` onto the **Crawl driver**; `PageCrawlLimitException` is no longer
thrown as control flow. Both `ISpider` consumers are affected — `ScraperEngine`
(updated here) and the distributed-worker pattern (`WebReaperSpider`, rewritten
as a Crawl-driver adapter in slice 4). Announced via this ADR + the CHANGELOG
migration section, consistent with the project's "called out loud, never
silent" rule. No compat shell: a forwarder returning `List<Job>` would have to
re-introduce the side channels this ADR exists to remove (the ADR-0009
precedent — a forwarder that reinstates the coupling defeats the deepening).

## Considered options

- **α — one Outstanding-work latch seam, two adapters; correctness from the
  visited-set atomic test-and-set (chosen).** Transport-independent (the
  strongest property; correctness derives from the store, not the queue). One
  mechanism, one home, two real adapters — the exact shape ADR
  0002/0003/0005/0006 each blessed. Reuses the seen-set this ADR already
  relocates to the driver; the correctness code is one atomic op + one CAS,
  both using primitives the project already has, both offline-testable through
  the seam.
- **β — a durable-workflow coordinator (Azure Durable Functions) for the
  distributed driver (rejected).** Genuinely the idiomatic Azure answer and
  exactly-once by event-sourced replay. Rejected because it buys "no
  hand-rolled correctness code" at the price of a **vendor-coupled runtime, a
  rewrite of the distributed worker, and a second, unrelated completion
  mechanism** (workflow distributed vs counter in-process) — precisely the
  duplication-with-drift smell every prior WebReaper ADR exists to kill.
  Recorded so future architecture reviews do not re-suggest it.
- **Emergent queue-drain for distributed completion (rejected).** A documented
  non-solution: scrapy-redis polls forever (`raise DontCloseSpider`;
  unimplemented sentinel), StormCrawler's FAQ states topologies "run
  continuously and there is no standard mechanism to determine when they should
  be terminated." "Queue momentarily empty" ≠ terminated is a theorem
  (Kshemkalyani & Singhal Ch. 7), not a heuristic.
- **An exact distributed page limit (rejected).** Requires a per-page
  distributed lock — expensive, defeats the purpose; soft/best-effort is the
  universal norm and already the de-facto in-process contract.
- **Transport duplicate-detection window as the sole correctness mechanism
  (rejected).** Service Bus dedup is window-bounded (default 10 min, max 7
  days); a Job redelivered after a DLQ retry outside the window silently
  double-counts. Defense-in-depth behind α only, never the mechanism.
- **A monotonic fencing token for the one-shot completion (deferred, not
  rejected).** Needed only if the end-of-crawl effect is external and
  non-idempotent. No such effect is in scope; the effect is idempotent
  (upsert keyed by run-id) + CAS'd. Named as a future concern so it is not
  silently assumed absent.
- **Keep the shape, just add `SpiderTests` (rejected — the framing fork).**
  The maintainer conceded the limit exception is incidental control flow with
  no returned channel; a test against today's `ISpider` cannot assert
  termination or emission because they are not on the interface. A
  coverage-only fix leaves the interface the wrong shape — the thing the
  candidate exists to correct.

## References

- `research/distributed-crawl-termination.md` — the evidence base (production
  crawlers, job-queue frameworks, the termination-detection literature;
  source-cited, fact/inference-flagged).
- ADR-0001 (the closed-returned-value move this ADR re-applies one layer up),
  ADR-0003/0005 (the Redis/Cosmos infra the distributed latch adapter reuses),
  ADR-0009 (the `BuildSpider()` bare-`ISpider` seam this ADR deepens the far
  side of).
