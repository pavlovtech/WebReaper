using WebReaper.Cdp;

namespace WebReaper.Cdp.Tests;

/// <summary>
/// ADR-0057. The per-CDP-session counter that backs
/// <c>PageAction.WaitForNetworkIdle</c>. Pins the four guarantees:
/// <list type="bullet">
///   <item>Idle from the start → returns after the debounce window.</item>
///   <item>Active in-flight → does not return early.</item>
///   <item>Drain to zero → returns once debounce elapses.</item>
///   <item>Total timeout → returns without throwing (ADR-0057 §Timeout).</item>
/// </list>
/// </summary>
public class NetworkActivityTests
{
    [Fact]
    public async Task Idle_from_the_start_returns_after_debounce()
    {
        var na = new NetworkActivity();
        // No request events ever fired. Already idle since instantiation.
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await na.WaitForIdleAsync(
            debounce: TimeSpan.FromMilliseconds(100),
            timeout: TimeSpan.FromSeconds(2),
            ct: default);
        sw.Stop();

        // Returned within the debounce window — but not instantly (debounce
        // is the minimum settle time before declaring idle). Allow some
        // scheduler jitter.
        Assert.True(sw.ElapsedMilliseconds <= 500,
            $"Idle wait took {sw.ElapsedMilliseconds}ms — debounce should resolve fast.");
        Assert.Equal(0, na.InFlight);
    }

    [Fact]
    public async Task Drain_to_zero_returns_after_debounce_elapses()
    {
        var na = new NetworkActivity();
        na.OnRequestStarted();
        Assert.Equal(1, na.InFlight);

        // After 100 ms, finish the request. Total wait should complete
        // shortly after debounce (200 ms) elapses post-drain.
        _ = Task.Run(async () =>
        {
            await Task.Delay(100);
            na.OnRequestFinished();
        });

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await na.WaitForIdleAsync(
            debounce: TimeSpan.FromMilliseconds(200),
            timeout: TimeSpan.FromSeconds(2),
            ct: default);
        sw.Stop();

        // Should be > drain(100) + debounce(200) = 300 ms, well under
        // timeout(2000).
        Assert.True(sw.ElapsedMilliseconds >= 250,
            $"Returned too early: {sw.ElapsedMilliseconds}ms");
        Assert.True(sw.ElapsedMilliseconds < 2000,
            $"Returned after timeout: {sw.ElapsedMilliseconds}ms");
        Assert.Equal(0, na.InFlight);
    }

    [Fact]
    public async Task Active_traffic_does_not_resolve_until_drained()
    {
        var na = new NetworkActivity();
        na.OnRequestStarted();

        // Wait should hit total timeout because in-flight never drains.
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await na.WaitForIdleAsync(
            debounce: TimeSpan.FromMilliseconds(50),
            timeout: TimeSpan.FromMilliseconds(300),
            ct: default);
        sw.Stop();

        // Approximately the timeout — should not have returned early.
        Assert.True(sw.ElapsedMilliseconds >= 250,
            $"Returned before timeout: {sw.ElapsedMilliseconds}ms");
        Assert.Equal(1, na.InFlight);
    }

    [Fact]
    public async Task Total_timeout_returns_without_throwing()
    {
        var na = new NetworkActivity();
        na.OnRequestStarted();  // never finished

        // Should return, NOT throw, per ADR-0057 §"Timeout semantics".
        await na.WaitForIdleAsync(
            debounce: TimeSpan.FromMilliseconds(50),
            timeout: TimeSpan.FromMilliseconds(100),
            ct: default);
        // Reaching here = no exception thrown.
    }

    [Fact]
    public async Task Cancellation_propagates_OperationCanceledException()
    {
        var na = new NetworkActivity();
        na.OnRequestStarted();
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(50);

        // ThrowIfCancellationRequested throws the base OperationCanceledException;
        // a Task.Delay-driven cancellation throws TaskCanceledException. Either
        // is valid — the test pins "cancellation propagates" not the exact subtype.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            na.WaitForIdleAsync(
                debounce: TimeSpan.FromMilliseconds(100),
                timeout: TimeSpan.FromSeconds(5),
                ct: cts.Token));
    }

    [Fact]
    public void Over_decrement_clamps_to_zero_not_below()
    {
        var na = new NetworkActivity();
        na.OnRequestFinished();   // unmatched
        na.OnRequestFinished();   // unmatched
        Assert.Equal(0, na.InFlight);

        na.OnRequestStarted();
        Assert.Equal(1, na.InFlight);
        na.OnRequestFinished();
        Assert.Equal(0, na.InFlight);
    }

    [Fact]
    public async Task New_request_during_debounce_resets_the_window()
    {
        var na = new NetworkActivity();
        // Initially idle for a moment, then a request fires.
        _ = Task.Run(async () =>
        {
            await Task.Delay(50);   // wait starts → debounce begins
            na.OnRequestStarted();   // resets debounce
            await Task.Delay(80);
            na.OnRequestFinished();  // drains; debounce restarts
        });

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await na.WaitForIdleAsync(
            debounce: TimeSpan.FromMilliseconds(100),
            timeout: TimeSpan.FromSeconds(2),
            ct: default);
        sw.Stop();

        // Must wait until: 50ms (request start) + 80ms (request finish) +
        // 100ms (debounce post-finish) = ~230ms.
        Assert.True(sw.ElapsedMilliseconds >= 200,
            $"Idle resolved during traffic: {sw.ElapsedMilliseconds}ms");
    }
}
