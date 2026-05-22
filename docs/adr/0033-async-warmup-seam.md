# Async adapter warm-up becomes an opt-in capability the Crawl driver drives; constructors stop doing async work

## Status

**Accepted — implementation complete** (2026-05-22; landed on branch
`adr-0033-async-warmup-seam` off `origin/master`). Eighth ADR of the
`/improve-codebase-architecture` review wave (after ADR-0026 through
ADR-0032, PRs #82-89, all merged), and **candidate #2 of the 2026-05-22
review** — the cleanest of the four that remained after ADR-0032.
Breaking: `IScheduler` and `IVisitedLinkTracker` (Tier-1 public seams,
ADR-0023) lose their `Initialization` member. Folds into the 10.0.0
wave the user is batching.

## Context

A durable adapter must do async work — connect, restore a cursor, wipe
the backing store on `DataCleanupOnStart` — **once, before it is first
used**. CONTEXT.md names no term for this; call it **adapter warm-up**.

Today warm-up is realized as Stephen Cleary's *asynchronous
initialization pattern*: the constructor fires `Initialization =
InitializeAsync()` and exposes a `Task Initialization { get; }`; a
consumer awaits the property before first use. The code is not ad-hoc —
it is that recognized pattern. But it is realized **inconsistently**,
and the inconsistency is the friction:

- **The member is on two of the three role interfaces.**
  [IScheduler.cs](../../WebReaper/Core/Scheduler/Abstract/IScheduler.cs)
  and
  [IVisitedLinkTracker.cs](../../WebReaper/Core/LinkTracker/Abstract/IVisitedLinkTracker.cs)
  declare `Task Initialization`. `IScraperSink` does not.
- **Ten durable adapters hand-roll `Initialization = InitializeAsync()`
  in the constructor:** `FileScheduler`, `RedisScheduler`,
  `SqliteScheduler`, `AzureServiceBusScheduler`;
  `FileVisitedLinkedTracker`, `RedisVisitedLinkTracker`,
  `SqliteVisitedLinkTracker`; `RedisSink`, `MongoDbSink`, `CosmosSink`.
- **The member's visibility drifted:** `CosmosSink.Initialization` is
  `public`; `MongoDbSink`'s and `RedisSink`'s are `private`.
- **Consumption is realized three ways.** (a) The in-process Crawl
  driver awaits `Scheduler.Initialization` + `LinkTracker.Initialization`
  once, up front, in `ScraperEngine.RunAsync`. (b) `SqliteScheduler` and
  `SqliteVisitedLinkTracker` *also* self-guard — every public method
  opens with `await Initialization` — so they are belt-and-suspenders:
  awaited by the driver **and** by themselves. (c) The three sinks
  self-guard *only* — every `EmitAsync` opens with `await
  Initialization`, and the driver never awaits them, because it cannot:
  the member is not on `IScraperSink`.
- Even a `Misc/` proxy provider (`WebShareProxyProvider`) independently
  reinvents `Task Initialization` — a fourth family, no shared home.

**The async work happens in the constructor.** `Initialization =
InitializeAsync()` makes construction *look* synchronous while it
launches async work whose exceptions surface only when something later
awaits the property. Cleary frames the property-and-ctor-fired-task
shape as the *fallback* — usable when an async factory is not — and the
constructor launch as its acknowledged compromise.

**The deletion test.** Delete the three sinks' `Initialization` /
`InitializeAsync` members: `RedisSink` and `MongoDbSink` lose their
`DataCleanupOnStart` wipe; `CosmosSink.EmitAsync` throws
`NullReferenceException` on `Container!` — Cosmos's warm-up is *required
setup* (it creates the database/container and caches the `Container`
handle), not optional cleanup. The warm-up need reappears in all three
→ a real concept.

Measured against
[LANGUAGE.md](../../.claude/skills/improve-codebase-architecture/LANGUAGE.md):
warm-up is a real concept (the deletion test passes) with many adapters
(a real *capability*, not a hypothetical seam), but it has **no seam** —
no named interface, no single **locality** for "warm me up." The root:
**warm-up is a lifecycle capability the domain has but no module
names.** It is intrinsic to no role — the in-memory scheduler, the
in-memory tracker, `ConsoleSink` and the file sinks have no warm-up at
all — so it drifted into a per-adapter, per-interface,
visibility-drifted, triply-consumed mess. This is the ADR-0002/0003/0006
*"copies drifted into a one-copy bug"* shape — here the drifted copy is
`IScraperSink`, and the bug it drifted into is the per-`EmitAsync`
self-guard the scheduler/tracker side never needed.

## Decision

Three moves.

### 1. Warm-up becomes an opt-in capability interface, `IAsyncInitializable`

A new Tier-1 public interface
([WebReaper/Infra/Abstract/IAsyncInitializable.cs](../../WebReaper/Infra/Abstract/IAsyncInitializable.cs),
beside `IRetryPolicy` — ADR-0026's sibling cross-cutting interface) with
one member:

```csharp
public interface IAsyncInitializable
{
    // Performed once, before the adapter is first used. Idempotent:
    // the first call warms up, every later call returns the same
    // completed task.
    Task InitializeAsync();
}
```

It is **orthogonal to the role interfaces** — the `IAsyncDisposable`
model. An adapter that warms up *also* implements `IAsyncInitializable`;
an adapter that does not (`InMemoryScheduler`,
`InMemoryVisitedLinkTracker`, `ConsoleSink`, the file sinks) implements
nothing. This is the codebase's own precedent: `AzureServiceBusScheduler`
already implements `IAsyncDisposable` — the opt-in async-*teardown*
capability — orthogonally to `IScheduler`. `IAsyncInitializable` is the
init-side mirror, and `InitializeAsync()` is parameterless exactly as
`DisposeAsync()` is.

`IScheduler` and `IVisitedLinkTracker` **lose** their `Initialization`
member. Warm-up is not intrinsic to *being a scheduler* — the in-memory
one has none. The BCL extends a role interface with a lifecycle
interface only when the lifecycle *is* intrinsic (`IEnumerator<T> :
IDisposable` — every enumeration has a cleanup point) and never does so
to `IEnumerable<T>` or `IList<T>`. `IScraperSink` is **unchanged**: the
candidate's named symptom — "`IScraperSink` lacks the member" —
dissolves, because warm-up was never a sink's role contract. A custom
`IScraperSink` is unaffected.

### 2. The Crawl driver drives warm-up, once, uniformly

`ScraperEngine.RunAsync` warms up every adapter it holds — the
scheduler, the visited-link tracker, every sink — that *is*
`IAsyncInitializable`, once, before the parallel crawl loop. It
type-tests (`adapter is IAsyncInitializable`) — the `await using`
idiom, AOT-safe, no reflection. The two inline `await
Scheduler.Initialization` / `await LinkTracker.Initialization` lines
become one warm-up routine that *also* covers the sinks the old code
never touched.

This is the `IHostedService.StartAsync` shape: the host — here the
in-process Crawl driver — constructs its components synchronously, then
drives their async startup before running. WebReaper does not depend on
`IHostedService` itself (it would drag `Microsoft.Extensions.Hosting`
and a `StopAsync` half the driver does not want); it models the start
half on it.

### 3. Constructors stop doing async work; `InitializeAsync` is idempotent

The ten durable adapters' constructors become pure — no `Initialization
= InitializeAsync()`. The async work is reached only through
`InitializeAsync()`, which is **idempotent**: the first call performs
warm-up, every later call returns the same completed task. It is backed
by `Lazy<Task>` — the BCL's thread-safe compute-once primitive, the
`AsyncLazy` idiom. The `Lazy<Task>` is *constructed* in the constructor
(trivial, synchronous); its factory runs on the first `InitializeAsync()`,
never in the constructor — so Cleary's pattern's one compromise, async
work launched from the constructor, is gone.

Idempotency is load-bearing twice:

- Several adapters' warm-up is **destructive** under `DataCleanupOnStart`
  — `SqliteScheduler` runs `DELETE FROM jobs`, `AzureServiceBusScheduler`
  runs `DeleteQueueAsync`. A `Lazy<Task>`-cached warm-up makes a
  double-call a safe no-op rather than a second wipe; strict call-once
  would re-run the destruction.
- It serves the **distributed driver**. The Azure Functions distributed
  driver is a per-message serverless function (ADR-0022 slice 4) — it
  has no single "before the crawl" moment the in-process driver has. It
  calls `InitializeAsync()` per message and, because the result is
  cached, pays exactly one warm-up round-trip across the whole crawl.

The **self-guards go.** `SqliteScheduler` / `SqliteVisitedLinkTracker`'s
per-method `await Initialization` and the three sinks' per-`EmitAsync`
`await Initialization` are deleted. Warm-up consumption — three patterns
today — collapses to one: **the owner calls `InitializeAsync()` once
before first use.** For the in-process driver that is the warm-up
routine of move 2; for the consumer-authored distributed driver it is
one idempotent call per message.

### Bounded scope — what this does NOT change

- **Each adapter's warm-up *work*.** Unchanged — only relocated from a
  private ctor-fired `InitializeAsync` to a private `InitializeCoreAsync`
  behind the `Lazy<Task>`.
- **Observable crawl behaviour.** Byte-identical. The driver still warms
  the scheduler + tracker before the loop, and now also the sinks —
  previously a sink self-warmed on its first `EmitAsync`; this is the
  same work moved one step earlier. `ScraperEngineDriverTests` and the
  sink tests pass unchanged.
- **The distributed driver stays consumer-authored** (ADR-0009).
  `WebReaper.AzureFuncs` is *updated* to call `InitializeAsync()`; no
  shared warm-up module is shipped for it — the ADR-0032 posture.
- **`IScraperSink`** — untouched. `IOutstandingWorkLatch`,
  `IRetryPolicy`, `IScraperConfigStorage`, `ICookiesStorage` — no
  warm-up adapters exist today; untouched. A future warming
  config-storage adapter extends the driver's routine by one line.
- **`Misc/WebReaper.ProxyProviders`** is not a packaged adapter
  (CLAUDE.md). `WebShareProxyProvider` is left as-is; `IProxyProvider`
  warm-up, if it ever matters, is a separate seam.

## Considered options

### (a) Minimal symmetry — add `Task Initialization { get; }` to `IScraperSink`

**Rejected.** It standardizes the antipattern (async-in-constructor,
now uniform across three interfaces) and still does not *name* warm-up
— the driver keeps special-casing each member. It has the same breaking
surface as naming the seam properly, so it is strictly dominated.

### (b) Role-interface base — all three role interfaces extend `IAsyncInitializable`

**Rejected.** No BCL precedent welds a *non-intrinsic* lifecycle onto a
role contract — the BCL does it only when intrinsic (`IEnumerator<T> :
IDisposable`), never to `IEnumerable<T>` / `IList<T>`. It forces a
`=> Task.CompletedTask` no-op on every warm-up-less adapter
(`InMemoryScheduler`, `ConsoleSink`, the file sinks) permanently — the
exact no-op member the in-memory adapters already carry. The one thing
it buys — warm-up cannot be forgotten — is weak: a forgotten warm-up
fails *fast and local* (a Cosmos `Container` `NullReferenceException` on
first emit), not as silent corruption, and the BCL made this exact call
for `IDisposable` / `IAsyncDisposable` (opt-in, despite "you can forget
to implement it").

### (c) Keep Cleary's property form — `IAsyncInitializable { Task Initialization { get; } }`

**Rejected.** It names the seam but keeps the compromise — async work
still launched from ten constructors. The explicit-method form is the
`IHostedService` shape, canonical for *this* situation (one driver owns
the components and starts them before one run), and it is what makes
the constructor genuinely pure.

### (d) `InitializeAsync(CancellationToken)`, strict call-once

**Rejected.** Strict call-once is fragile against the destructive
adapters — a second call re-runs `DELETE FROM jobs` /
`DeleteQueueAsync` — and it has no answer for the serverless distributed
driver, which has no single call-once site and would hand-roll a
once-gate. Idempotent + parameterless mirrors
`IAsyncDisposable.DisposeAsync()` — the codebase's existing capability
interface, on `AzureServiceBusScheduler` — survives a double-call, and
serves both drivers with no special case. A per-call `CancellationToken`
also does not compose with a `Lazy<Task>`-cached result; warm-up is
short and fire-once, and you no more cancel a warm-up than a dispose.

### (e) Async factory methods / `AsyncLazy<T>` that return the built adapter

The preferred canonical async-construction pattern — it never exposes
an uninitialized instance. **Rejected as the seam:** WebReaper's
adapters are built inside a synchronous fluent builder chain
(`.WriteToConsole().WithRedisScheduler(...)`) and by satellite
extension methods — there is no `await` point to host a factory. (The
`Lazy<Task>` *inside* each adapter is the `AsyncLazy` idiom, applied to
warm-up rather than to construction.)

### (f) A shared `IAsyncInitializable` consumed by the distributed driver too

Ship a warm-up coordinator both drivers call. **Rejected** for
ADR-0032's reason: the distributed driver is consumer-authored
boilerplate (ADR-0009); a module a consumer must remember to call does
not prevent drift. The shared *primitive* is the interface and the
idempotent contract; the in-process driver's warm-up routine is
in-process; the distributed consumer drives warm-up itself, one
idempotent call per message.

### (g) An abstract base class holding the `Lazy<Task>` machinery

Have the ten adapters extend a base class that owns the cached-task
idempotency. **Rejected as unnecessary:** `Lazy<Task>` is *already* the
shared, BCL-provided machinery — the per-adapter footprint is one field
plus one expression-bodied `InitializeAsync`, wiring constrained by the
interface (it cannot drift in visibility or name the way the old
property did), not logic. A base class buys nothing `Lazy<Task>` does
not, and C#'s single inheritance would complicate
`AzureServiceBusScheduler` (already `: IScheduler, IAsyncDisposable`).
This is unlike ADR-0023's payload-shell bases, which carry real shared
*serialization* logic; warm-up's only shared part is the idempotency
primitive, and the BCL ships it.

## Consequences

- **Warm-up has a name and a seam.** `IAsyncInitializable` is the one
  home for the capability; the Crawl driver's warm-up routine is the one
  home for *driving* it. The concept the deletion test found now has a
  module.
- **Constructors do no async work.** The ten adapters construct purely;
  `Lazy<Task>` defers warm-up to the first `InitializeAsync()`. There is
  no ctor-launched task whose exceptions hide until a later await.
- **Warm-up consumption collapses from three patterns to one.** The
  driver-awaits / Sqlite-belt-and-suspenders / sink-self-guard
  trichotomy becomes: the owner calls `InitializeAsync()` once. Ten
  self-guards are deleted; the public/private visibility drift is
  unrepresentable — an interface member is public.
- **The driver warms the sinks it never warmed before.** A sink used to
  self-initialize on its first `EmitAsync`; now the driver warms every
  `IAsyncInitializable` sink before the crawl loop — the same work, one
  step earlier, uniform with the scheduler and tracker.
- **`IScheduler` + `IVisitedLinkTracker` are a breaking change** — the
  `Initialization` member is removed (Tier-1 public seams, ADR-0023). A
  custom durable scheduler / tracker that relied on the driver awaiting
  its `Initialization` must implement `IAsyncInitializable` instead;
  until it does, the driver no longer awaits its warm-up before the
  crawl. SemVer **major** — folds into the unreleased 10.0.0 wave.
  `IScraperSink` is unchanged: no break for custom sinks.
- **`IAsyncInitializable` is additive** — a new public interface; a
  custom adapter that needs no warm-up is unaffected.
- **The distributed driver's warm-up is named, not centralized.**
  `WebReaper.AzureFuncs` is updated to call `InitializeAsync()`;
  idempotency lets the per-message function warm up cheaply. ADR-0022's
  consumer-authored distributed-driver posture stands.
- **Warm-up is testable offline.** New tests pin the contract: the Crawl
  driver calls `InitializeAsync` on an `IAsyncInitializable` sink before
  the first `EmitAsync`, and `InitializeAsync` is idempotent — the work
  runs once across repeated calls.
- **CONTEXT.md** gains **Adapter warm-up** as a defined term; a new
  "Flagged ambiguities" bullet records the decision.

## Implementation

Landed on `adr-0033-async-warmup-seam`:

1. **`WebReaper/Infra/Abstract/IAsyncInitializable.cs`** (new) — the
   opt-in capability interface; one parameterless idempotent
   `InitializeAsync()`.
2. **Ten adapters** — `FileScheduler`, `RedisScheduler`,
   `SqliteScheduler`, `AzureServiceBusScheduler`,
   `FileVisitedLinkedTracker`, `RedisVisitedLinkTracker`,
   `SqliteVisitedLinkTracker`, `RedisSink`, `MongoDbSink`, `CosmosSink`
   — pure constructors; `: IAsyncInitializable`; a `Lazy<Task>` field +
   `public Task InitializeAsync() => _initialization.Value;` + the
   former `InitializeAsync` body renamed to private
   `InitializeCoreAsync`; the ten self-guards (`SqliteScheduler` ×3,
   `SqliteVisitedLinkTracker` ×4, the three sinks ×1) removed.
3. **`IScheduler.cs` / `IVisitedLinkTracker.cs`** — `Initialization`
   removed.
4. **`InMemoryScheduler.cs` / `InMemoryVisitedLinkTracker.cs`** — the
   no-op `Initialization` removed.
5. **`WebReaper/Core/ScraperEngine.cs`** — `RunAsync` warms up the
   scheduler, the tracker and every sink that is `IAsyncInitializable`,
   once, before `Parallel.ForEachAsync`; the two inline `await
   …Initialization` lines are gone.
6. **`Examples/WebReaper.AzureFuncs`** — `WebReaperSpider` and
   `StartScraping` call `InitializeAsync()` on the adapters they drive.
7. **Tests** — 14 `await x.Initialization` call sites become `await
   x.InitializeAsync()`; `ScraperEngineDriverTests` gains two cases
   pinning the driver-warms-before-emit contract and `InitializeAsync`
   idempotency under concurrent calls.
8. **`CONTEXT.md`** — **Adapter warm-up** term added; one new "Flagged
   ambiguities" bullet.

### Guardrails (run on the branch tip)

- `dotnet build WebReaper.sln` — **0 errors**; 19 warnings, all
  pre-existing (CS8618 nullable-field, CS8633 logger-generic-variance,
  CS1574, xUnit1031, NU1510) — unchanged in set and count from before
  ADR-0033. Core's `WarningsAsErrors=CS1591` stays green: the new public
  `IAsyncInitializable` carries its XML docs.
- `dotnet test WebReaper.Tests/WebReaper.UnitTests` — **163/163 pass**
  (161 pre-0033 + two new `ScraperEngineDriverTests` cases: the driver
  warms an `IAsyncInitializable` sink before the first `EmitAsync`, and
  `InitializeAsync` idempotency under concurrent calls).
- `dotnet test WebReaper.Tests/WebReaper.Sqlite.Tests` — **10/10 pass**:
  the offline durable-adapter satellite — `SqliteScheduler` /
  `SqliteVisitedLinkTracker` exercise the pure-ctor + `Lazy<Task>` +
  renamed-warm-up migration end to end.
- The satellite + `Examples` projects all compile in the whole-solution
  build (`WebReaper.AzureFuncs`'s distributed driver updated to call
  `InitializeAsync`). The network-backed satellite suites (`Redis` /
  `Mongo` / `Cosmos` / `AzureServiceBus`) and the live-site
  `WebReaper.IntegrationTests` run on CI. AOT is unaffected — the change
  adds no reflection, serialization, or dynamic codegen; the driver's
  `is IAsyncInitializable` is a static interface type-test.

## References

- ADR-0022 — the Crawl driver constructs and drives the per-crawl
  adapters; this ADR gives it a uniform warm-up step before the loop.
- ADR-0023 — the Tier-1 / Tier-2 split: `IAsyncInitializable` is a
  Tier-1 public interface; `IScheduler` / `IVisitedLinkTracker` are
  Tier-1 public seams, so removing their `Initialization` member is a
  documented breaking change.
- ADR-0026 — `IRetryPolicy`, the sibling cross-cutting capability
  interface, and the `WebReaper.Infra.Abstract` home this one shares.
- ADR-0009 — the distributed driver is consumer-authored; why move 2 is
  in-process and the distributed driver drives its own warm-up.
- ADR-0032 — the "shared primitive, not a shared module the consumer
  must remember to call" posture, reused here.
