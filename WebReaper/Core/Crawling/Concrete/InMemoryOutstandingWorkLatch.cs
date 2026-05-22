using WebReaper.Core.Crawling.Abstract;

namespace WebReaper.Core.Crawling.Concrete;

/// <summary>
/// In-process Outstanding-work latch (ADR-0022): the inline <c>Interlocked</c>
/// counter behind the seam. Exact by construction — a single
/// <see cref="Interlocked.Add(ref int, int)"/> applies the net credit change
/// and returns 0 to exactly one thread, so <see cref="SignalProcessedAsync"/>
/// trips for exactly one caller.
/// </summary>
internal sealed class InMemoryOutstandingWorkLatch : IOutstandingWorkLatch
{
    private int _pending;

    public Task SeedAsync(int startJobCount)
    {
        Interlocked.Exchange(ref _pending, startJobCount);
        return Task.CompletedTask;
    }

    /// <summary>Credit the children and return this Job's unit in one atomic
    /// step (net <c>childCount - 1</c>); trips iff outstanding credit hit
    /// zero.</summary>
    public Task<bool> SignalProcessedAsync(int childCount)
        => Task.FromResult(Interlocked.Add(ref _pending, childCount - 1) == 0);
}
