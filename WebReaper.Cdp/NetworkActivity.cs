namespace WebReaper.Cdp;

/// <summary>
/// Per-CDP-session counter of in-flight network requests (ADR-0057). Fed by
/// the <see cref="CdpClient"/> read loop's event branch on
/// <c>Network.requestWillBeSent</c> (++), <c>Network.loadingFinished</c> /
/// <c>Network.loadingFailed</c> / <c>Network.requestServedFromCache</c> (−−);
/// awaited by <see cref="CdpClient.WaitForNetworkIdleAsync"/>.
/// </summary>
/// <remarks>
/// One tracker per session. The transport currently uses one session per
/// navigation (ADR-0052), so the tracker matches a navigation's lifetime.
/// <para>
/// Counter floor is clamped at zero — a malformed event stream (a stray
/// <c>loadingFinished</c> without a paired <c>requestWillBeSent</c>) over-
/// decrements toward false-idle rather than under-decrements toward
/// never-idle. The asymmetry is deliberate (ADR-0057 §"Accepted cost").
/// </para>
/// </remarks>
internal sealed class NetworkActivity
{
    private readonly object _lock = new();
    private int _inFlight;
    private DateTime _lastActivityUtc = DateTime.UtcNow;

    /// <summary>Current in-flight request count — for tests and diagnostics.</summary>
    public int InFlight { get { lock (_lock) return _inFlight; } }

    /// <summary>Called by the read loop on <c>Network.requestWillBeSent</c>.</summary>
    public void OnRequestStarted()
    {
        lock (_lock)
        {
            _inFlight++;
            _lastActivityUtc = DateTime.UtcNow;
        }
    }

    /// <summary>Called by the read loop on <c>Network.loadingFinished</c>,
    /// <c>Network.loadingFailed</c>, or <c>Network.requestServedFromCache</c>.</summary>
    public void OnRequestFinished()
    {
        lock (_lock)
        {
            _inFlight = Math.Max(0, _inFlight - 1);
            _lastActivityUtc = DateTime.UtcNow;
        }
    }

    /// <summary>Block until <see cref="InFlight"/> has been zero for
    /// <paramref name="debounce"/>, or <paramref name="timeout"/> elapses.
    /// Returns normally on either outcome — the caller logs a timeout
    /// warning if it wants to distinguish (ADR-0057 §"Timeout semantics").
    /// </summary>
    /// <remarks>
    /// Implementation is polling at a 50 ms resolution (or finer when close
    /// to the debounce edge). Polling is preferred over a Timer + TCS here
    /// to avoid a finalizer-disposal complication on a per-navigation tracker;
    /// the cost is one short sleep per cycle while traffic is active.
    /// </remarks>
    public async Task WaitForIdleAsync(TimeSpan debounce, TimeSpan timeout, CancellationToken ct)
    {
        var startedAt = DateTime.UtcNow;
        var deadline = startedAt + timeout;

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            int currentInFlight;
            DateTime lastActivity;
            lock (_lock)
            {
                currentInFlight = _inFlight;
                lastActivity = _lastActivityUtc;
            }

            if (currentInFlight == 0)
            {
                // Idle since: the later of (wait-start, last activity). A
                // wait that started after the page already went idle measures
                // debounce from the wait's own start, not from a stale
                // activity timestamp.
                var idleSince = lastActivity > startedAt ? lastActivity : startedAt;
                var idleFor = DateTime.UtcNow - idleSince;
                if (idleFor >= debounce) return;

                // Sleep the remaining debounce window — bounded to 50 ms so
                // we recheck quickly if traffic resumes mid-sleep.
                var remaining = debounce - idleFor;
                var sleepMs = Math.Max(10, Math.Min(50, remaining.TotalMilliseconds));
                await Task.Delay(TimeSpan.FromMilliseconds(sleepMs), ct);
            }
            else
            {
                // Active — poll at the same 50 ms resolution until in-flight
                // drains.
                await Task.Delay(TimeSpan.FromMilliseconds(50), ct);
            }
        }
        // Total timeout — return without throwing (per ADR-0057). The caller
        // logs the warning if it cares.
    }
}
