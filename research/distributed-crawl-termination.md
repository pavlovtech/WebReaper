# Distributed crawl termination & exactly-once completion

> **Date:** 2026-05-18 · **Status:** Evidence base for the Crawl-driver deepening (grill of "Spider shell test seam" → deepening (B)); feeds a forthcoming ADR.
> **Method:** three independent research tracks — (1) production distributed crawlers, (2) distributed job/queue frameworks, (3) the CS theory of termination detection + exactly-once over at-least-once. Convergence across all three is the signal. **[FACT]** = quoted from a cited primary/authoritative source; **[INFERENCE]** = application of that to WebReaper, labelled.

## The question

Under deepening (B) the **Crawl driver** (not the per-Job **Spider** shell) owns Crawl-global state. There are two driver adapters: in-process (`ScraperEngine`, `Parallel.ForEachAsync`) and distributed (Azure Function, one Service Bus message = one Job, at-least-once). The user requires: a distributed Crawl must **stop cleanly when work runs out** and fire a reliable end-of-crawl action. How?

## 1. "Queue empty ≠ done" is a theorem, not a heuristic

**[FACT]** Kshemkalyani & Singhal, *Distributed Computing* (CUP 2008), Ch. 7: a computation is terminated iff *all processes idle **and no message in transit in any channel**.* The second conjunct is load-bearing; a detector that ignores in-flight work is unsound by construction. (EWD998 TLA⁺ formalises the same: `terminated == \A n : ~active[n] /\ pending[n] = 0`.)

**[FACT]** Every production crawler that detects completion *correctly* encodes exactly this invariant:
- **Heritrix**: `isEmpty()` is true only when "all queues are empty **and** no URIs are in-process" (`getInProcessCount()==0`); single manager thread is the sole evaluator. ([src](https://github.com/internetarchive/heritrix3/blob/master/engine/src/main/java/org/archive/crawler/frontier/AbstractFrontier.java))
- **Crawlee**: `isFinished()` is deliberately distinct from `isEmpty()` — "even if the queue is empty, there might be some pending requests currently being processed." Distributed mode: "may occasionally return a **false negative, but it shall never return a false positive**." ([API](https://crawlee.dev/js/api/core/class/RequestQueue))
- **Colly**: a `sync.WaitGroup` incremented before each (recursively spawned) request, decremented after — an in-process outstanding-work counter.

**[FACT]** Frameworks that rely on drain/idle **explicitly do not solve distributed completion and say so**: scrapy-redis polls Redis forever (`raise DontCloseSpider`; the `# XXX: Handle a sentinel to close the spider` is unimplemented), only a *per-worker local idle timer*, no cross-worker coordination. StormCrawler FAQ: topologies "run continuously and there is no standard mechanism to determine when they should be terminated." → **Emergent queue-drain is a known non-solution.** The user's requirement cannot be met by "workers idle out."

**[FACT]** Real systems push the decision to a **single coordinator** that is effectively a singleton (Heritrix manager thread; Frontera Strategy worker; Nutch driver script) — which is also how they get near-exactly-once completion.

## 2. WebReaper's `pending` counter is already the right algorithm

**[INFERENCE, from theory track]** WebReaper's existing in-process mechanism (`ScraperEngine.cs:46-97`: seed=N; per Job, `pending += #children` **before** the Job is marked done, then `pending -= 1`; zero ⇒ done) is **not** Dijkstra–Scholten. It is a **centralized unit-credit termination detector** (Huang 1989 / Mattern 1989, degenerate case). The integer-count form even sidesteps Mattern's float-underflow wart — a genuine advantage of counting units over splitting a weight.

**[FACT]** Its exact correctness precondition is credit conservation: **register children + add their credit, then decrement the parent, then ack — atomically, each step exactly once.** The existing code comment *"Account for children BEFORE this job is marked done, so the counter can never hit zero prematurely"* **is** this precondition. Decrement-before-add (or ack-before-account) is a textbook **orphan-message false termination** (Bosilca et al., *Revisiting Credit Distribution Algorithms*).

→ The in-process design is theoretically sound. The deepening is to recognise the counter as a **seam** with two adapters, not to invent an algorithm.

## 3. The at-least-once hazard, and the fix (convergent across all three tracks)

**[FACT]** Azure Service Bus is at-least-once; duplicate detection is **window-bounded** (default 10 min, **max 7 days**) — outside the window, duplicates are not caught. SQS standard / Lambda likewise ("make your function idempotent"). Raw counting over this is **unsound**:

| Redelivery | Counter effect | Outcome |
|---|---|---|
| `-= 1` applied twice | under-decrement | **premature zero → false termination** (safety — the dangerous one) |
| `+= #children` twice | over-mint | never reaches zero → **never completes** (liveness) |

**[FACT]** Celery's chord is the cautionary tale: a blind `INCR` per part-return with no idempotency gate → documented (#4412) premature firing **and lost tasks** under exactly this redelivery.

**[FACT]** The frameworks that are correct over at-least-once **do not trust a raw increment** — they gate the mutation on a stable identity: NServiceBus **Outbox** dedups by `MessageId` in the same transaction as saga state ("processed once and only once"); BullMQ uses Redis **set-membership** (re-removal is a no-op); Sidekiq keys the count on `jid`. Temporal / **Azure Durable Functions** sidestep counters entirely via **event-sourced replay** (completed activities are replayed, never re-executed → at-least-once activity execution cannot corrupt completion).

**[FACT, theory track, ranked #1] The best-fit fix is literally the mechanism (B) already implies:** make the accounting idempotent via an **atomic test-and-set on a seen-set as the single idempotency authority** — perform the counter mutation **iff** the Job's first insert into the seen-set succeeds; counter-mutation and set-insert in **one atomic op** (Redis `MULTI`/Lua, or a Cosmos transactional batch on a shared partition key). This makes detection correct **independent of the transport's delivery semantics** — correctness derives from the store's atomic CAS, not the queue. WebReaper already has the seen-set: `IVisitedLinkTracker`, which (B) already relocates to the Crawl driver. Dedup, the limit gate, and completion accounting all ride one atomic membership check. Transport dedup windows = defense-in-depth only, never the sole mechanism.

## 4. The one-shot completion action (separate sub-problem)

**[FACT]** Even a correct detector must fire the action exactly once. Canonical treatments, best-first: (a) **idempotent effect** — upsert a completion record keyed by run-id; double-fire is harmless (Kafka EOS "effect applied once"); (b) **CAS'd one-shot** — `SET completion_fired NX` in the *same atomic op* that observes `counter == 0`; only the winner runs it (primitives WebReaper already has); (c) **fencing token** (monotonic run-epoch) only if the effect is external/non-idempotent and cannot be deduped at the sink. Exactly-once *effect*, not exactly-once *delivery*.

## 5. Limit is soft everywhere — decision already taken, now corroborated

**[FACT]** Scrapy `CLOSESPIDER_*`, Crawlee `maxRequestsPerCrawl` ("can be slightly higher … requests in progress … still finished"), Heritrix budgets, Colly `MaxDepth` — all soft pre-dispatch gates that overshoot under concurrency. Only Nutch's discrete `num_rounds` is exact. The "soft/best-effort, bounded by concurrency, uniform across both drivers" decision is the universal norm, not a compromise.

## The decision this leaves open: α vs β

- **α — Counter-as-seam.** One credit/latch seam, two adapters: in-memory `Interlocked` (in-process) + distributed-atomic (Redis Lua / Cosmos transactional batch). Correctness via the `IVisitedLinkTracker` atomic test-and-set as the single idempotency authority; CAS'd one-shot completion. Transport-agnostic. *One* mechanism, one home, ≥2 real adapters — the exact shape every prior WebReaper ADR (0002/0003/0005/0006) blesses. Cost: we own + test the atomic op and the CAS.
- **β — Durable-workflow coordinator.** Distributed driver becomes an Azure Durable Functions orchestrator; exactly-once completion *by construction* via replay; idiomatic Azure (framework track's "canonical Azure answer"). Cost: a vendor-coupled runtime; rewrites the distributed worker; the in-process driver still needs its own mechanism ⇒ **two unrelated completion mechanisms** — the duplication-with-drift smell every WebReaper ADR exists to kill.

**Recommendation: α.** Its correctness is transport-independent (strongest property, what we want); it is one mechanism with two adapters (the project's ADR-blessed posture) vs β's two; it reuses the seen-set (B) already relocates. β's genuine edge (no hand-rolled correctness code) is real only if the distributed deployment is exclusively Azure and a vendor-coupled second mechanism is acceptable.

## Sources

Crawlers: Heritrix `AbstractFrontier` src; Crawlee `RequestQueue` API; Colly `colly.go`; scrapy-redis `spiders.py`; StormCrawler FAQ; Apache Nutch `bin/crawl`. Job queues: Celery canvas + issue #4412; Sidekiq Batches wiki + #4685; BullMQ Flows; Temporal "Understanding Temporal"; **Azure Durable Functions** fan-out/fan-in + orchestrations (Microsoft Learn); NServiceBus Sagas + Outbox; MassTransit Job Consumers; Hangfire Batches. Theory: Dijkstra–Scholten EWD684 (1980); Safra/Dijkstra EWD998 + `tlaplus-workshops/ewd998`; Huang 1989; Mattern 1989 (via Bosilca et al., *Revisiting Credit Distribution Algorithms*); Kshemkalyani & Singhal Ch. 7; Azure Service Bus duplicate-detection (Microsoft Learn); AWS SQS standard-queue + Lambda idempotency; Confluent delivery-semantics.
