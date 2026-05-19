using System.Collections.Immutable;
using WebReaper.Core.LinkTracker.Abstract;

namespace WebReaper.Core.LinkTracker.Concrete;

/// <summary>
/// The default <see cref="IVisitedLinkTracker"/>: an in-process
/// <see cref="ImmutableHashSet{T}"/> behind a lock-free CAS. It overrides the
/// seam's non-atomic default with a genuine atomic test-and-set, so it is the
/// in-process idempotency authority (ADR-0022). Single-process only (state is
/// not shared or persisted — use the File / Redis tracker for that). Also an
/// in-memory building block for the ADR-0009 DIY-distributed pattern.
/// </summary>
public class InMemoryVisitedLinkTracker : IVisitedLinkTracker
{
    /// <inheritdoc/>
    public bool DataCleanupOnStart { get; set; }

    private ImmutableHashSet<string> visitedUrls = ImmutableHashSet.Create<string>();

    /// <inheritdoc/>
    public Task AddVisitedLinkAsync(string visitedLink)
    {
        ImmutableInterlocked.Update(ref visitedUrls, set => set.Add(visitedLink));

        return Task.CompletedTask;
    }

    // ADR-0022 slice 2: the exact idempotency authority. Lock-free atomic
    // test-and-set on the immutable set — only the thread whose CAS installs a
    // set that newly contains the link returns true; concurrent duplicates
    // (and, distributed-side later, redeliveries) observe it present and
    // return false. Replaces the seam's check-then-add default with no race
    // window.
    /// <inheritdoc/>
    public Task<bool> TryAddVisitedLinkAsync(string visitedLink)
    {
        var spin = new SpinWait();
        while (true)
        {
            var current = Volatile.Read(ref visitedUrls);
            if (current.Contains(visitedLink)) return Task.FromResult(false);

            var updated = current.Add(visitedLink);
            if (ReferenceEquals(Interlocked.CompareExchange(ref visitedUrls, updated, current), current))
                return Task.FromResult(true);

            spin.SpinOnce();
        }
    }

    /// <inheritdoc/>
    public Task<List<string>> GetVisitedLinksAsync()
    {
        return Task.FromResult(visitedUrls.ToList());
    }

    /// <inheritdoc/>
    public Task<List<string>> GetNotVisitedLinks(IEnumerable<string> links)
    {
        return Task.FromResult(links.Except(visitedUrls).ToList());
    }

    /// <inheritdoc/>
    public Task<long> GetVisitedLinksCount()
    {
        return Task.FromResult((long)visitedUrls.Count);
    }

    /// <inheritdoc/>
    public Task Initialization => Task.CompletedTask;
}
