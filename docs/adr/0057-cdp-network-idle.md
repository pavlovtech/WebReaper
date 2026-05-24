# `PageAction.WaitForNetworkIdle` becomes a CDP `Network.*` event tracker on the CDP transport; the 500 ms settle hack is retired

## Status

**Accepted — implementation complete** (2026-05-24). v10.x **Transports
cleanup wave** sibling to [ADR-0056](0056-cli-bot-check-escalation.md)
and [ADR-0058](0058-engine-teardown-disposal.md). Replaces the
`Task.Delay(500)` placeholder that shipped in the CDP transport's
`PageAction.WaitForNetworkIdle` arm (ADR-0052 §"PageAction dispatch")
with the real behaviour the Puppeteer transport's `WaitForNetworkIdle2`
delivered before deletion.

## Context

ADR-0035 closed the `PageAction` sum with seven arms; `WaitForNetworkIdle`
is the one whose semantics are *temporal* rather than *positional*. The
Puppeteer transport (deleted in ADR-0053) implemented it by delegating
to PuppeteerSharp's `Page.WaitForNetworkIdleAsync` — a wrapper around
Puppeteer's `networkidle0` / `networkidle2` predicates, which in turn
track CDP `Network.requestWillBeSent` / `Network.responseReceived` /
`Network.loadingFinished` / `Network.loadingFailed` events and resolve
when the in-flight request count has stayed at zero (or ≤2) for a
configurable settle window.

ADR-0052 shipped the CDP transport with a `Task.Delay(500)` placeholder
on the `WaitForNetworkIdle` arm and a comment naming this ADR as the
follow-up. The placeholder is wrong in both directions:

- **False idle.** 500 ms is well under the time XHR / fetch chains
  routinely take on JS-heavy pages. A scrape that depends on
  `WaitForNetworkIdle` to flush the page's data fetches returns before
  they arrive — the extractor sees an incomplete DOM.
- **Wasted time.** On a page that *did* finish loading instantly
  (cached, no XHR), the 500 ms is dead wait — every page action of
  this kind pays it.

The CDP transport already calls `Network.enable` per session
(ADR-0052), so the events are flowing on the existing browser-level
WebSocket. What's missing is a per-session counter that the read loop
increments / decrements, plus a wait primitive that drains when the
counter hits zero and stays there for a debounce window.

CONTEXT.md's **Load transport** glossary names two transports (HTTP,
CDP) post-Puppeteer-deletion; the *quality* of the CDP transport's
PageAction dispatch table is what makes it a true replacement, not
just a substitute.

## Decision

### The per-session tracker — on the CDP client

A new public method on `CdpClient`:

```csharp
public Task WaitForNetworkIdleAsync(
    string sessionId,
    TimeSpan? debounce = null,           // default 500 ms
    TimeSpan? timeout = null,            // default 30 s
    CancellationToken ct = default);
```

Backed by per-session state held in a `ConcurrentDictionary<string,
NetworkActivity>` on the client. The state is two counters
(`InFlight`, generation number for debounce restarts) plus a
`TaskCompletionSource<object?>` the read loop completes when:

- `InFlight == 0` *and* `debounce` has elapsed since the last
  `Network.requestWillBeSent` (or since `WaitForNetworkIdleAsync`
  started, whichever is later).

The CDP read loop's existing event branch (`CdpClient.ReadLoopAsync`)
gains four method-name cases:

```csharp
// in ReadLoopAsync's event branch, before pushing onto _eventQueue:
var method = parsed["method"]?.GetValue<string>();
var sId = parsed["sessionId"]?.GetValue<string>();
if (sId is not null && method is not null && _networkTrackers.TryGetValue(sId, out var t))
{
    switch (method)
    {
        case "Network.requestWillBeSent":     t.OnRequestStarted();    break;
        case "Network.loadingFinished":
        case "Network.loadingFailed":         t.OnRequestFinished();    break;
        case "Network.requestServedFromCache": t.OnRequestServedFromCache(); break;
    }
}
// Still push the event onto _eventQueue — WaitForEventAsync consumers unaffected.
```

The tracker is **registered lazily** by `WaitForNetworkIdleAsync`: the
first call for a sessionId allocates a `NetworkActivity` and adds it
to the dictionary; subsequent calls reuse it (re-arming the debounce
window). The tracker is **deregistered** when the session is detached
(implicitly on `Target.closeTarget`, explicitly when the transport
finalises a page request in its `finally` block) — a `RemoveTracker`
call sweeps the entry, so a long-lived browser WebSocket with many
short-lived sessions doesn't grow the dictionary unboundedly.

`NetworkActivity` itself is a small `sealed class` with an internal
lock guarding `InFlight` and the debounce state. The
in-flight-and-debounced check runs both on the
`OnRequestFinished` decrement (which may transition to idle) and on a
timer the wait starts when `InFlight == 0` (which fires the
`TaskCompletionSource` after `debounce` ms).

### What counts as "a request"

All CDP `Network.*` events flow through the same tracker. Specifically:

- **Increment** on `Network.requestWillBeSent` — any subresource
  (XHR, fetch, document, stylesheet, image, ws). The Puppeteer
  shape was the same; the WebReaper-shaped predicate is "the page
  has stopped fetching", not "data-fetch XHRs settled".
- **Decrement** on `Network.loadingFinished` or `Network.loadingFailed`
  (the two terminal events). Also on `Network.requestServedFromCache`
  — CDP doesn't emit `loadingFinished` for cache hits.
- **Ignore** `Network.responseReceived` — it's mid-stream, not
  terminal. Counting it would double-count and miss connection-error
  closures.

This matches Puppeteer's `networkidle0`. The transport's
`WaitForNetworkIdle` arm does not currently expose the `networkidle2`
variant (in-flight ≤ 2 instead of 0); this ADR keeps the same arm
shape (`PageAction.WaitForNetworkIdle` has no parameters today) and
defers a future arm widening.

### The transport's arm dispatch

`CdpPageLoadTransport.PerformAsync`'s `WaitForNetworkIdle` case
changes from:

```csharp
case PageAction.WaitForNetworkIdle:
    await Task.Delay(500, ct);
    break;
```

to:

```csharp
case PageAction.WaitForNetworkIdle:
    await browser.WaitForNetworkIdleAsync(sessionId, ct: ct);
    break;
```

The defaults (500 ms debounce, 30 s total timeout) live on the client;
no parameter change at the PageAction surface; no behaviour delta on
the consumer side except *the action actually works*.

### Timeout semantics — log, don't throw

On total-timeout expiry, the method logs at `Warning` and returns
normally (parity with `Page.loadEventFired` timeout in
`CdpPageLoadTransport.LoadAsync` — see ADR-0052 line 113). The
PageAction never aborts the navigation: a long-poll connection (SSE,
WebSocket, infinite stream) shouldn't kill the scrape; the consumer
gets a warning and the rest of the action chain runs against whatever
DOM is there. Throwing would change the contract from "WaitForNetworkIdle
is a settle hint" to "WaitForNetworkIdle is an assertion"; the v10
shape stays a hint.

### AOT-cleanness

No new reflection, no new dynamic dispatch. The tracker uses:

- `ConcurrentDictionary<string, NetworkActivity>` — generic, AOT-clean.
- `TaskCompletionSource<object?>` — sealed type, AOT-clean.
- `System.Threading.Timer` — for the debounce callback. AOT-clean.

The `WebReaper.AotSmokeTest` need not change for this ADR — the smoke
test exercises the *client*, not the *event* path; adding a CDP-event
smoke would need a fake WebSocket harness, which is the value
`WebReaper.Cdp.Tests` (ADR-0052's follow-up, queue item #4 in the
handoff, shipping in the same wave PR as this ADR) provides instead.

## Considered options

- **Per-session CDP tracker on the client, ≤0 in-flight + debounce
  (chosen).** Matches Puppeteer's `networkidle0` shape; minimal
  state; no new public surface (the tracker is client-internal; the
  PageAction arm is unchanged).
- **Run a parallel `WaitForEventAsync` loop with manual counting in
  the transport (rejected).** Doable but duplicates the read-loop's
  event dispatch; the tracker would race with the event queue
  consumers. Single owner (the read loop) is the cleaner shape.
- **`networkidle2` as a separate arm (`WaitForNetworkAlmostIdle`)
  (rejected, deferred).** ADR-0035's closed sum widens by ADR — adding
  arms is structural, not a flag toggle. v10.x keeps the one arm; a
  future ADR may add an in-flight-threshold parameter if a demonstrated
  use case appears (one observed in the user's audiobook scenario or
  in community PRs).
- **Wait for the browser's `Page.frameStoppedLoading` event instead of
  tracking network requests (rejected).** Fires on the main frame's
  load, not on settled fetches. A page that loaded then started an
  XHR storm would report "stopped" while still fetching. Strictly
  weaker.
- **Wire Microsoft.Playwright's `WaitForLoadStateAsync` shape into the
  Playwright satellite first, then mirror (rejected, scope).** The
  Playwright satellite's `WaitForNetworkIdle` arm is unrelated to this
  ADR's CDP-specific tracker — Playwright has its own primitive. Each
  transport's arm dispatches via its own SDK; the duplication is the
  ADR-0052 accepted cost. Out of scope to align them.

## Accepted cost

- **Tracker is per-session, not per-target-tree.** Sub-frames of the
  page that have their own sessions (cross-origin iframes via
  `Page.frameAttached` + `Target.attachToTarget`) would each carry
  their own counter. v10.x's transport uses one session per
  navigation; sub-frame attachment is out of scope here. If a future
  ADR adds sub-frame tracking, the tracker's keying widens to a frame
  tree; not today.
- **Counter is non-strict against malformed event streams.** A
  `loadingFinished` without a matching `requestWillBeSent` (a CDP
  bug, or a sessionId mismatch) decrements past zero — the floor
  clamp is `Math.Max(0, count - 1)`. Wrong direction (over-decrement
  → false idle) is preferred to the wrong direction in the other case
  (over-increment → never-idle hang). Matches Puppeteer's tolerance.
- **No HTTP-status-from-Network surface on this ADR.** The tracker
  hooks `Network.*` events but does not capture
  `Network.responseReceived` for status-code surfacing — that's the
  ADR-0056 follow-up named there. Both ADRs need to widen
  `CdpClient`'s event hooks in the same place; ADR-0056 stays
  deferred to keep the ADRs decoupled.

## Deliberate consequences

- **The CDP transport's PageAction table reaches behavioural parity
  with the deleted Puppeteer transport.** `WaitForNetworkIdle` was
  the last arm with a placeholder body; this ADR closes it. Future
  consumers migrating off Puppeteer (the deletion landed in v10.0.0)
  no longer hit a `Task.Delay(500)` regression on this action.
- **`CdpClient` gains the first event-counting primitive of its
  category.** Future temporal predicates (e.g. a
  `WaitForJsConsoleMessage` arm, or an LLM-driven semantic wait that
  needs "the page settled") can build on the same per-session-tracker
  pattern. The shape is the template; the dictionary is the registry.
- **`WebReaper.Cdp.Tests`'s `CdpClient` harness gets a tested
  primitive.** The tests for this ADR feed scripted `Network.*` events
  through a fake WebSocket and assert the wait completes when the
  counter drains; the same harness is what queue item #4 of the
  handoff (general CDP dispatch tests) needs. The two ship together.

## SemVer

**Patch.** No public-surface change; pure behaviour upgrade on
`PageAction.WaitForNetworkIdle`. Behaviour delta is a *fix* of the
shipped placeholder. v10.0.0's major is owned by ADR-0053.
