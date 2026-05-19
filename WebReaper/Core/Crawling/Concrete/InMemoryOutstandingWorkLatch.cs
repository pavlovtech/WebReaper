using WebReaper.Core.Crawling.Abstract;

namespace WebReaper.Core.Crawling.Concrete;

/// <summary>
/// In-process Outstanding-work latch (ADR-0022): the slice-1 inline
/// <c>Interlocked</c> counter, now behind the seam. Exact by construction —
/// <see cref="Interlocked.Decrement(ref int)"/> returns 0 to exactly one
/// thread, so <see cref="SignalProcessedAsync"/> trips for exactly one caller.
/// </summary>
public sealed class InMemoryOutstandingWorkLatch : IOutstandingWorkLatch
{
    private int _pending;

    public Task SeedAsync(int startJobCount)
    {
        Interlocked.Exchange(ref _pending, startJobCount);
        return Task.CompletedTask;
    }

    public Task AddAsync(int childCount)
    {
        if (childCount != 0) Interlocked.Add(ref _pending, childCount);
        return Task.CompletedTask;
    }

    public Task<bool> SignalProcessedAsync()
        => Task.FromResult(Interlocked.Decrement(ref _pending) == 0);
}
