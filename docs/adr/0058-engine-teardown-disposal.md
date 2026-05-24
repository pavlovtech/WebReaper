# `ScraperEngine` becomes `IAsyncDisposable`; the disposal chain mirrors ADR-0033's warm-up; builder-spawned satellite processes (CloakBrowser, …) tear down on engine teardown

## Status

**Accepted — implementation complete** (2026-05-24). v10.x **Transports
cleanup wave** sibling to [ADR-0056](0056-cli-bot-check-escalation.md)
and [ADR-0057](0057-cdp-network-idle.md). Pins the disposal side of
the adapter lifecycle that ADR-0033 (`IAsyncInitializable`) opened on
the warm-up side, and closes the named v10.x gap from CLAUDE.md
Gotchas: "`WithCloakBrowser()` currently leaves the spawned
CloakBrowser process running until WebReaper exits (OS-reaped)".

## Context

ADR-0033 introduced **owner-driven async warm-up**: every adapter that
needs to do async work before its first use implements
`IAsyncInitializable`, and the in-process `ScraperEngine` walks every
adapter it holds and calls `InitializeAsync()` once, before the crawl
loop. The contract is clean: constructors do no async work; warm-up is
opt-in; the engine is the owner.

The **disposal side** of that lifecycle was not built. Adapters
implementing `IAsyncDisposable` exist (every Redis/MongoDb/Sqlite/Cosmos
adapter; every CDP / Playwright transport — see
`WebReaper.Cdp.CdpPageLoadTransport` line 22 + line 302 in v10.0.0),
but `ScraperEngine` itself is not `IAsyncDisposable`, and the engine's
held adapters are never disposed on engine teardown. Pre-Transports-wave
this leaked but was diffusely visible — durable adapters with explicit
disposal patterns (Redis connections, Sqlite handles) were rare in the
in-process scrape path the CLI shipped.

The Transports wave changes the picture sharply:

1. **`WithCloakBrowser()` spawns a subprocess at builder time.** The
   `CloakBrowserLauncher.LaunchAsync(...)` returns a
   `LaunchedCdpEndpoint` (CDP URL + `IAsyncDisposable` teardown
   handle); ADR-0054's extension method discards the handle:

   ```csharp
   // current: handle dropped
   var endpoint = CloakBrowserLauncher.LaunchAsync(path, opts).GetAwaiter().GetResult();
   return b.WithCdpPageLoader(endpoint.CdpUrl);
   ```

   The result: a 220 MB stealth-Chromium process per `BuildAsync()`
   that lives until the WebReaper host exits (OS reaps on
   `dotnet test` teardown; a long-running serverless function leaks
   it). CLAUDE.md flags this explicitly.

2. **The CDP transport's `_disposeUrlProvider` callback is already the
   right hook** — ADR-0052's launch-and-connect overload uses it for
   the managed-Chromium spawn. The hook exists; nothing calls it
   because nobody disposes the transport.

3. **The Playwright satellite's `IBrowser` handle** (ADR-0053) needs
   the same treatment: the satellite's `PlaywrightBrowser` owns a
   `Microsoft.Playwright.IPlaywright` + `IBrowser`; both must close on
   teardown or the host's Node-driver process and the spawned browser
   leak.

The shape that fixes all three is the dual of ADR-0033: **owner-driven
async disposal**, walked in reverse warm-up order.

## Decision

### `ScraperEngine` implements `IAsyncDisposable`

```csharp
public class ScraperEngine : IAsyncDisposable
{
    private readonly List<IAsyncDisposable> _ownedDisposables = new();
    private bool _disposed;

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        // Reverse warm-up order: processors → sinks → tracker → scheduler →
        // spider (whose chain reaches the page loader, the transport, and
        // any satellite-spawned resources).
        foreach (var p in PageProcessors.Reverse())
            await DisposeIfAsync(p);
        foreach (var s in Sinks.AsEnumerable().Reverse())
            await DisposeIfAsync(s);
        await DisposeIfAsync(LinkTracker);
        await DisposeIfAsync(Scheduler);
        await DisposeIfAsync(Spider);

        // Builder-registered owned disposables — satellite-spawned
        // processes (CloakBrowser, Playwright IBrowser, …) and anything
        // a future satellite wires via b.OnTeardown(...).
        for (var i = _ownedDisposables.Count - 1; i >= 0; i--)
            try { await _ownedDisposables[i].DisposeAsync(); }
            catch (Exception ex) { Logger.LogWarning(ex, "Disposal of {Type} threw", _ownedDisposables[i].GetType().Name); }
    }

    private static async ValueTask DisposeIfAsync(object? o)
    {
        if (o is IAsyncDisposable a) await a.DisposeAsync();
        else if (o is IDisposable d) d.Dispose();
    }
}
```

The `_ownedDisposables` list is populated by the builder during
`BuildAsync()` (next subsection). The engine's reverse-warm-up walk
keeps adapter dependencies valid: a sink may write during a
processor's flush, so processors dispose first; the spider's transport
may still emit network teardown CDP commands, so the spider disposes
*after* sinks but *before* the satellite-process handle.

**Disposal exceptions are swallowed-with-log, not thrown.** A scrape
that ran to completion should not retroactively fail because a Redis
connection's teardown timed out; the warning surfaces in the log
without re-throwing through `await using`.

### Builder-side: `ScraperEngineBuilder.OnTeardown(IAsyncDisposable)`

New internal hook on `ScraperEngineBuilder`:

```csharp
public ScraperEngineBuilder OnTeardown(IAsyncDisposable disposable)
{
    _teardownHooks.Add(disposable);
    return this;
}
```

Satellite extensions call it. `WithCloakBrowser()` becomes:

```csharp
public static ScraperEngineBuilder WithCloakBrowser(
    this ScraperEngineBuilder b, CloakBrowserOptions? opts = null)
{
    opts ??= new CloakBrowserOptions();
    var path = CloakBrowserInstaller.EnsureInstalledAsync(opts.InstallOptions).GetAwaiter().GetResult();
    var endpoint = CloakBrowserLauncher.LaunchAsync(path, opts).GetAwaiter().GetResult();
    return b
        .WithCdpPageLoader(endpoint.CdpUrl)
        .OnTeardown(endpoint);  // the LaunchedCdpEndpoint is IAsyncDisposable; engine kills the process on teardown
}
```

`ScraperEngineBuilder.BuildAsync()` hands the collected list to the
engine constructor; the engine adopts the hooks into
`_ownedDisposables`. A consumer using `await using var engine = await
builder.BuildAsync(); await engine.RunAsync();` gets the
satellite-process kill on scope exit.

### Existing engine constructor stays; new optional parameter

```csharp
internal ScraperEngine(
    /* existing params */,
    IReadOnlyList<IAsyncDisposable>? ownedDisposables = null)
{
    /* ... */
    if (ownedDisposables is not null) _ownedDisposables.AddRange(ownedDisposables);
}
```

Optional, defaulted — the existing call sites in tests and the
distributed-driver pattern (ADR-0009) work unchanged. The builder is
the only caller that passes a non-empty list in v10.x.

### Idempotence + double-dispose tolerance

`DisposeAsync` guards on `_disposed` and short-circuits; same shape as
`CdpPageLoadTransport.DisposeAsync` already uses. A consumer wrapping
the engine in both `await using` and an explicit
`await engine.DisposeAsync()` (a defensive double-call) sees the
second call as a no-op. Same idempotence guarantee ADR-0033's
`InitializeAsync` carries.

### Distributed driver — explicitly opt-in to the same chain

The ADR-0009 distributed-driver pattern constructs adapters by hand
and never goes through `ScraperEngineBuilder`. Consumers driving
adapters directly are responsible for their own disposal — same as
they're responsible for their own warm-up (ADR-0033 §Distributed
driver). The `WebReaper.DistributedSpiderBuilder` follow-up can mirror
the `OnTeardown` hook if the pattern earns rent there; v10.x ships
the chain on the in-process `ScraperEngineBuilder` only.

### Library-consumer migration

The recommended pattern in README + CHANGELOG:

```csharp
// New (v10.x):
await using var engine = await builder.BuildAsync();
await engine.RunAsync();

// Old (still works; leaks satellite resources on builder-time spawn):
var engine = await builder.BuildAsync();
await engine.RunAsync();
```

The CLI's `ScrapeCommand.RunAsync` migrates to `await using`. The
existing in-tree examples migrate in the same PR; community consumers
get a release-note nudge but no breaking change.

## Considered options

- **Owner-driven `IAsyncDisposable` chain on `ScraperEngine`, reverse
  warm-up order, builder-hook for satellite extensions (chosen).**
  Direct dual of ADR-0033 — same shape, opposite arrow. The
  `OnTeardown` hook closes the named CLAUDE.md gotcha without
  widening `ScraperEngineBuilder`'s public surface meaningfully
  (existing satellite extensions are the only callers in v10).
- **Disposal as a finalizer (`~ScraperEngine`) (rejected).**
  GC-triggered, non-deterministic; the satellite-process kill needs
  to happen synchronously on scope exit, not whenever the runtime
  decides. Async work in finalizers is a known anti-pattern.
- **Force every satellite to register an `IAsyncDisposable` on a
  global registry (rejected).** Static state crosses engine
  instances; a per-process global breaks the
  multi-engine-in-one-process scenarios (background scheduler running
  N parallel scrapes). Per-engine ownership is the right scope.
- **Push disposal into the page-loader factory (rejected).** The
  factory returns the transport; the transport already implements
  `IAsyncDisposable`; the factory delegate signature has no place
  to hang an extra "and also dispose this thing" callback without
  becoming a 6-arg tuple. The transport's own `_disposeUrlProvider`
  closure handles its own resources; this ADR's hook handles
  satellite-builder-time resources orthogonal to the transport.
- **Make `ScraperEngine` `IDisposable` (sync) instead of
  `IAsyncDisposable` (rejected).** Several owned disposables are
  natively async (CDP WebSocket close, Redis connection close,
  Sqlite connection async close). Sync would block on
  `.GetAwaiter().GetResult()` — well-known deadlock risk in
  ASP.NET / serverless contexts. Async is the right shape.
- **Throw on first disposal exception (rejected).** A consumer
  scrape that succeeded should not surface a teardown failure as
  the run's overall outcome. The fan-out semantics: each disposal
  attempt logs its own failure; the run's return code is the
  scrape's return code, not the teardown's. Mirrors ADR-0026's
  retry-policy "log and continue" stance on fault recovery.

## Accepted cost

- **`ScraperEngine` gains a tiny mutable list.** `_ownedDisposables`
  is per-engine state; null-defaulted on the constructor; populated
  only when the builder has hooks. Memory cost: one list, ≤ a
  handful of entries in v10.x usage. Negligible.
- **Builder API gains one new method (`OnTeardown`).** Public surface
  +1. The method is opt-in for satellite extensions, not a contract
  every adapter must implement; only the satellites that
  *builder-time-spawn* a resource need it. v10.x: one caller
  (`WithCloakBrowser`); v10.x+1: likely the Playwright satellite's
  `WithPlaywrightPageLoader` once it stops short-circuit-disposing
  via its own `IAsyncDisposable` field.
- **Disposal-exception swallow is a deliberate-bug-hider risk.**
  A consistently-failing disposal would log on every scrape and
  potentially mask a real resource leak. The log line is at
  `Warning` (not `Error`) — visible in the default logger config —
  but a consumer ignoring warnings would miss it. Mitigation: the
  per-satellite README's "Troubleshooting" section flags the log;
  the alternative (throw-through-`await using`) is worse for
  scrape-succeeded-teardown-burped scenarios.
- **The distributed-driver shape stays asymmetric.** Owner-driven
  warm-up is on the in-process engine + the consumer-authored
  distributed driver; owner-driven disposal is on the in-process
  engine only. The distributed-driver consumer must dispose
  their own adapters. Documented in ADR-0009's pattern doc; same
  divergence ADR-0033 accepted.

## Deliberate consequences

- **The audiobook scenario actually completes cleanly.** A user
  running `await using var engine = await builder.WithCloakBrowser().BuildAsync()`
  in a CLI tool gets the stealth-Chromium subprocess killed on scope
  exit — not OS-reaped after the host dies. Resource lifecycle
  matches the engine lifecycle; matches every other resource the
  engine touches.
- **The CLAUDE.md gotcha closes.** The named "WithCloakBrowser leaves
  the process running" line gets struck through in the same PR that
  lands this ADR; the gotcha section gains a one-line "ADR-0058
  resolved this" note for archeological reference.
- **Future satellites that spawn at builder time have a documented
  hook.** A `WithLocalBrightDataAgent()`, a hypothetical
  `WithCamoufox()`, anything that owns a subprocess —
  `b.OnTeardown(spawnedHandle)` is the one-line addition. The
  pattern lives in ADR-0054's recipe (added in the same PR).
- **`IAsyncInitializable` and `IAsyncDisposable` become the
  symmetric pair the lifecycle was missing.** ADR-0033 is the *door
  opened*; ADR-0058 is the *door closed*. CONTEXT.md's adapter
  glossary widens to call out both sides.

## SemVer

**Minor (additive).** `ScraperEngine` adds `IAsyncDisposable` —
purely additive (an `await using` on the engine now does something;
the absence is the pre-existing behaviour and still works). Builder
adds `OnTeardown` — purely additive. v10.0.0's major is owned by
ADR-0053; this rides v10.x as a minor.

## v2 deferrals

- **Distributed-driver disposal hook.** Mirror the builder pattern on
  `DistributedSpiderBuilder` once a satellite spawns at distributed-
  build time (no current case).
- **Disposal observability seam.** A future ADR may expose a
  `EngineDisposed` event / hook so an observability sink can
  capture "how long did teardown take" / "which adapter failed".
  Out of scope for the v10.x cleanup wave; would need a real
  observability ADR to compose into (ADR-0018 was Proposed but not
  Accepted at draft-time).
- **Sync `IDisposable` mirror on the engine.** Some hosts (Topshelf,
  certain Windows services) prefer sync disposal. Could ship a
  `ScraperEngine.Dispose()` that blocks-and-disposes; deferred to
  a community-requested follow-up.
