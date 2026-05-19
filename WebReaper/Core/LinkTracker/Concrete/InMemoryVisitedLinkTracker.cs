using System.Collections.Immutable;
using WebReaper.Core.LinkTracker.Abstract;

namespace WebReaper.Core.LinkTracker.Concrete;

public class InMemoryVisitedLinkTracker : IVisitedLinkTracker
{
    public bool DataCleanupOnStart { get; set; }
    
    private ImmutableHashSet<string> visitedUrls = ImmutableHashSet.Create<string>();

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
    
    public Task<List<string>> GetVisitedLinksAsync()
    {
        return Task.FromResult(visitedUrls.ToList());
    }

    public Task<List<string>> GetNotVisitedLinks(IEnumerable<string> links)
    {
        return Task.FromResult(links.Except(visitedUrls).ToList());
    }

    public Task<long> GetVisitedLinksCount()
    {
        return Task.FromResult((long)visitedUrls.Count);
    }

    public Task Initialization => Task.CompletedTask;
}