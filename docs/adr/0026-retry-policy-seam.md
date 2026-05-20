# Retry around the per-Job Spider call becomes a named seam; Polly leaves core

## Status

**Accepted — implementation complete** (2026-05-20; landed on branch
`adr-0026-retry-policy-seam` off `origin/master` d565f79,
awaiting merge — fresh `/improve-codebase-architecture` review wave, see
*Implementation status* at the end). Additive on the public surface,
internal-only refactor of the wrapper, one observable behaviour delta
(cancellation no longer pays three wasted retries) which is the latent-bug
fix. Folds naturally into whatever release the user batches next; no
dedicated SemVer wave required.

## Context

ADR-0022 made the **Crawl driver** the home of everything the per-Job
**Spider** shell deliberately is not: Visited-link tracker, crawl-limit
stop, Sink fan-out, Outstanding-work latch. It also kept one detail from
the pre-7.0 design verbatim: the driver wraps the Spider call in
`Infra.Executor.RetryAsync`, a 14-line static `internal` wrapper over
`Polly.Policy.Handle<Exception>().RetryAsync(3)` ([WebReaper/Infra/
Executor.cs](../../WebReaper/Infra/Executor.cs)). One call site,
[ScraperEngine.cs:166](../../WebReaper/Core/ScraperEngine.cs):

```csharp
var report = await RetryAsync(() => Spider.CrawlAsync(job, cancellationToken));
```

Measured against LANGUAGE.md, this is the textbook **shallow** module:

- **Interface** = nearly as complex as **Implementation**. `RetryAsync<T>(Func<Task<T>>)`
  + the static `Retry<T>(Action)` overload (whose `<T>` is unused, with zero
  callers — dead from the day it shipped). The body is one Polly chain.
- **Adapters: zero.** Not even one — it is a static helper, not behind an
  interface. By LANGUAGE.md's *"one adapter means a hypothetical seam, two
  adapters means a real one"*, this is sub-hypothetical.
- **Leverage = none.** The Crawl driver still must conceptually know "this
  retries on any exception, three times, no backoff." The wrapper renames
  the operation; it does not abstract behaviour.
- **The catch-all was load-bearing — and is no longer.** Pre-ADR-0022 the
  Spider threw `PageCrawlLimitException` to terminate; `Handle<Exception>()`
  was the catch path that the driver tested for. ADR-0022 made termination
  a value, removed `PageCrawlLimitException`, and left the catch-all in
  place: it now retries deterministic failures (a malformed schema's
  `RequireSelector` throw, a parser bug) three times before propagating
  them — pure waste, no longer load-bearing.
- **Cancellation is silently swallowed three times.** `Handle<Exception>()`
  matches `OperationCanceledException` and `TaskCanceledException`. A
  user cancelling mid-crawl waits up to three further Spider invocations
  before the cancellation propagates to the driver's downstream catch.
  This is a latent bug, not the intended ADR-0022 cancellation flow.
- **Polly is in core.** [WebReaper.csproj:74](../../WebReaper/WebReaper.csproj)
  carries `<PackageReference Include="Polly" Version="8.6.6" />`, brought
  in solely for `Executor`. ADR-0009's dependency-light principle ("the
  dependency enters the consumer's graph only when they reference the
  satellite") is contradicted in core by one transitive Polly graph for
  one 14-line wrapper.

The deletion test on `Infra.Executor` alone gives the textbook **(a)
complexity vanishes** answer: inline the Polly chain at the one call site
and nothing reappears anywhere. But pure deletion would silently remove
retries — a behaviour change worse than the friction it cures. The right
answer is the deepening: turn the Crawl driver's retry from a hidden
implementation detail into a named contract callers can vary.

The three real variations that exist *today*, not hypothetically:

1. **Core default** — fixed N attempts, no backoff. The current behaviour
   minus the cancellation-swallow bug.
2. **No-retry** — unit and integration tests want the Spider's first throw
   to surface immediately rather than be silently swallowed and retried.
   Today's `Infra.Executor` has no way to express this; every Spider unit
   test pays three retries on every deliberate-failure case (it doesn't,
   because no unit test today constructs the engine end-to-end past the
   retry — *because* it isn't testable, the friction has been hidden).
3. **Custom** — exponential backoff with jitter (the industry-standard
   network-retry shape); a Polly resilience pipeline brought by the
   consumer; a future Puppeteer-aware policy that retries on
   `NavigationException` but not on a 404.

Three variations is above the LANGUAGE.md threshold ("two adapters means a
real seam"). The seam earns its keep.

## Decision

Replace `Infra.Executor` with a **Retry policy** — one Tier-1 seam, one
Tier-2 core default, registered via the Registration seam.

1. **The seam.** New `public interface IRetryPolicy` in
   `WebReaper.Infra.Abstract` (the `Abstract`/`Concrete` split the rest of
   the codebase uses):

   ```csharp
   namespace WebReaper.Infra.Abstract;

   public interface IRetryPolicy
   {
       Task<T> ExecuteAsync<T>(
           Func<CancellationToken, Task<T>> action,
           CancellationToken cancellationToken);
   }
   ```

   The token is passed *to* the action so the policy can short-circuit
   (cooperative cancellation between attempts), not just closed over by
   the caller. Tier-1, fully documented per ADR-0023's contract bar.

2. **The default core adapter.** New `internal sealed class
   FixedAttemptsRetryPolicy : IRetryPolicy` in `WebReaper.Infra.Concrete`:
   ctor takes `int maxAttempts = 4` (four total attempts = one initial +
   three retries, the exact pre-0026 Polly behaviour); the loop is a
   `for (var attempt = 1; ; attempt++)` with
   `cancellationToken.ThrowIfCancellationRequested()` at the head and a
   `catch when (attempt < _maxAttempts)` filter — no `OperationCanceledException`
   in the retry path. No delay (matches pre-0026; backoff is a *custom*
   policy concern, not the core default's). Tier-2 by ADR-0023's deletion
   test (named by no consumer; reached only through the builder).

3. **Registration.** `ScraperEngineBuilder.WithRetryPolicy(IRetryPolicy)` —
   purely additive on the public Registration seam. Internally
   `SpiderBuilder` carries the field; `ScraperEngineBuilder.BuildAsync`
   passes it to the engine ctor. `DistributedSpiderBuilder` is **out of
   scope**: the ADR-0009 reduced shell returns a bare `ISpider`; retry is
   the distributed worker's concern (Service Bus / Redis own redelivery).

4. **The Crawl driver.** `ScraperEngine` takes an `IRetryPolicy` ctor
   parameter (`internal` ctor — non-breaking on the public surface, see
   ADR-0023). The single call site at `ScraperEngine.cs:166` becomes:

   ```csharp
   var report = await retryPolicy.ExecuteAsync(
       token => Spider.CrawlAsync(job, token), cancellationToken);
   ```

5. **Polly leaves core.** Delete `WebReaper/Infra/Executor.cs`. Drop
   `<PackageReference Include="Polly" Version="8.6.6" />` from
   `WebReaper.csproj`. The hand-rolled fixed-attempts loop is the only
   thing core needed Polly for; consumers wanting a Polly resilience
   pipeline bring `Polly` *in their own project* and wrap it in an
   `IRetryPolicy` adapter.

6. **Cancellation behaviour is fixed by construction.** The default
   adapter never catches `OperationCanceledException`; the token is
   checked at the head of every attempt. The latent three-retry
   cancellation-swallow goes away.

The change is internal-only on the public surface (Executor was already
Tier-2 internal per ADR-0023); the one observable behaviour delta is the
cancellation fix, which is desirable.

## Considered options

### (a) Delete Executor outright, inline Polly at the one call site

The deletion-test answer if all we cared about was the wrapper. Rejected:
silently inlining doesn't fix the cancellation bug, doesn't remove Polly
from core (Polly is still referenced from `ScraperEngine`), and doesn't
give tests a way to skip retries. Maximally cheap, minimally useful.

### (b) Keep Executor, just fix the cancellation bug + add a configuration knob

`Executor.RetryAsync(int maxAttempts = 4, …)` with a Polly filter that
excludes `OperationCanceledException`. Rejected: leaves Polly in core,
leaves the static-helper shape (no interface), no second adapter (still
hypothetical seam). Solves the cheapest bug, leaves the deeper friction.

### (c) Deepen into a *resilience* seam (retry + backoff + circuit breaker)

Mirror `Microsoft.Extensions.Resilience` directly: `IResiliencePipeline`
with retry, timeout, circuit-breaker, hedging. Rejected for now:
**WebReaper has exactly one operation that needs resilience** — the
per-Job Spider call — and it has never had a circuit breaker or hedging.
LANGUAGE.md's "two adapters means a real seam" applies; one operation
across one collaborator is a *narrow* seam, not a *broad* one. Naming
the broad concept (resilience) when only the narrow one (retry) is in
play would be aspirational over-design. A consumer who wants
circuit-breaker semantics writes an `IRetryPolicy` adapter that wraps a
`ResiliencePipeline` — six lines.

### (d) Push retry down into `IPageLoader` (transient HTTP errors only)

Make retry the loader's concern: HTTP retries on 5xx with backoff
internally, the Crawl driver's loop is unguarded. Rejected: the Spider
does more than load a page — it runs `CrawlStep.StepAsync` (parse), and
parse failures (a malformed JSON response, a transient parse error) are
real causes of retry-worthy failures, not just HTTP ones. Localising
retry to the loader leaves parse / scheduler-edge failures uncovered.
Also, the loader is one of two transports today (HTTP + Puppeteer) — each
would need to learn retry, against the ADR-0004 "load mechanism only"
posture.

### (e) Make `WithRetryPolicy` available on `DistributedSpiderBuilder` too

Symmetry would be tidy. Rejected: a `DistributedSpiderBuilder` consumer
already owns the per-message-handle scope (the worker function or
ServiceBus handler); their retry knob is the queue's redelivery /
visibility-timeout / max-receive-count. Adding an in-process retry inside
the per-message handle compounds badly with queue-level retry and is the
distributed-systems "double-retry storm" anti-pattern. The seam stays
where the single-process driver is.

## Consequences

- **The Crawl driver's retry behaviour is a documented contract**, not
  an inlined Polly chain. Users vary it via the Registration seam; tests
  vary it for offline determinism.
- **Core loses one dependency** (Polly 8.6.6). `IsAotCompatible` stays
  true; nothing in `Polly` was load-bearing for AOT (its only use was the
  one chain). One less transitive graph in every consumer's `.csproj`.
- **One latent cancellation bug closes.** A user cancelling mid-crawl no
  longer pays three wasted Spider invocations.
- **Test surface widens, by construction.** `FixedAttemptsRetryPolicy`
  has unit tests against `IRetryPolicy`'s interface — first-try success,
  Nth-try success, exhaustion, cancellation pre-call, cancellation
  mid-call, the `OperationCanceledException` action-throws-it path,
  ctor validation. A `NoRetryPolicy` test double makes
  `ScraperEngineDriverTests` deterministic on deliberate-failure cases
  (it can now assert "first throw wins" without paying three retries).
- **CONTEXT.md grows by one term** — **Retry policy** — and one
  relationship line. CLAUDE.md's two mentions of `Infra.Executor.RetryAsync`
  update to point at the seam. `WebReaper.csproj`'s `PackageReleaseNotes`
  for the next release records the (additive) `WithRetryPolicy` knob and
  the (observable) cancellation-fix.
- **No SemVer bump needed beyond the existing 10.0.0 wave** — additive
  on the public surface, internal-only refactor of the wrapper. Folds
  naturally into whatever release the user batches next.

## Implementation status

All ten planned changes landed in one commit on
`adr-0026-retry-policy-seam`:

1. ✅ `WebReaper/Infra/Abstract/IRetryPolicy.cs` — Tier-1 public interface,
   documented to ADR-0023's contract bar (summary, remarks, every param
   and exception). Implementations *must* propagate
   `OperationCanceledException` without retrying and rethrow the last
   non-cancellation exception on exhaustion.
2. ✅ `WebReaper/Infra/Concrete/FixedAttemptsRetryPolicy.cs` — Tier-2
   `internal sealed`. Four attempts default, ctor throws on
   `maxAttempts < 1`, `for (;;)` loop with head-of-iteration
   `cancellationToken.ThrowIfCancellationRequested()`,
   `catch (OperationCanceledException) { throw; }` + a
   `catch when (attempt < _maxAttempts)` filter — cancellation is never
   caught for retry, by construction.
3. ✅ `WebReaper/Builders/SpiderBuilder.cs` — private
   `IRetryPolicy RetryPolicy { get; set; } = new FixedAttemptsRetryPolicy();`
   field, `WithRetryPolicy(IRetryPolicy)` method, internal
   `DriverRetryPolicy` property the engine builder reads.
4. ✅ `WebReaper/Builders/ScraperEngineBuilder.cs` — `WithRetryPolicy`
   public registration (XML-documented for IntelliSense); `BuildAsync`
   passes `SpiderBuilder.DriverRetryPolicy` to the engine ctor named arg.
5. ✅ `WebReaper/Core/ScraperEngine.cs` — `internal` ctor gains
   `IRetryPolicy? retryPolicy = null` (defaults to a fresh
   `FixedAttemptsRetryPolicy()` when omitted); the line-166 call site
   becomes `RetryPolicy.ExecuteAsync(token => Spider.CrawlAsync(job, token),
   cancellationToken)`. `using static WebReaper.Infra.Executor;` removed.
6. ✅ `WebReaper/Infra/Executor.cs` — deleted.
7. ✅ `WebReaper/WebReaper.csproj` — `<PackageReference Include="Polly"
   Version="8.6.6" />` removed; the deletion is annotated with an XML
   comment pointing at this ADR so a future packager doesn't reintroduce
   it by reflex.
8. ✅ `WebReaper.Tests/WebReaper.UnitTests/RetryPolicyTests.cs` — ten
   tests pinning the contract at the interface: first-attempt success,
   Nth-attempt success, exhaustion propagates the last exception,
   `OperationCanceledException`/`TaskCanceledException` thrown by the
   action propagate immediately, token cancelled pre-call throws without
   invoking the action, token cancelled between attempts short-circuits
   remaining attempts, `maxAttempts: 1` means no retry, ctor validation
   on zero/negative `maxAttempts`, null-action throws.
9. ✅ `CONTEXT.md` — **Retry policy** definition added under "The Crawl
   driver"; one new Relationship line; one new "Flagged ambiguities"
   bullet pinning the decision and the four rejected paths so future
   reviews don't re-suggest them; the ADR-0022 narrative bullet gets a
   small clarifying parenthetical pointing forward, and the ADR-0023
   Tier-2 list's `Executor` mention is annotated with the ADR-0026
   retirement.
10. ✅ `CLAUDE.md` — the *Run path* paragraph now names the
    **Retry policy** and points at this ADR; the two test files
    (`SpiderTests.cs`, `ScraperEngineDriverTests.cs`) update their
    historical-narrative comments to reference the new seam instead of
    the deleted `Executor`.

### Guardrails (run on the branch tip)

- `dotnet build WebReaper.sln` — **0 errors, 17 warnings** (every warning
  pre-existing on `origin/master`; no new warning attributable to this
  ADR). `WarningsAsErrors=CS1591` on core therefore green: the new
  Tier-1 `IRetryPolicy` carries its XML doc.
- `dotnet test WebReaper.sln --no-build` (non-Integration) — **123/123
  pass**: 104 unit (94 pre-0026 + 10 new `RetryPolicyTests`) + 10 Sqlite
  + 4 Puppeteer + 3 Mongo + 1 Cosmos + 1 AzureServiceBus.
- `WebReaper.AotSmokeTest` — `dotnet publish -c Release` succeeds with
  no `IL2xxx`/`IL3xxx` warnings (the scoped `WarningsAsErrors` list);
  the published native binary runs and prints `AOT SMOKE: ALL PASS` for
  all 8 round-trip cases. Polly leaving core does not change the AOT
  picture (Polly was AOT-friendly), but it is one less PackageReference
  in every consumer's transitive graph.
- Live-site `WebReaper.IntegrationTests` not run on the branch — they
  hit `alexpavlov.dev` with real Puppeteer/Chromium and `Task.Delay` up
  to 25 s, slow and environmentally flaky; the CI workflow runs them on
  the PR.
