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