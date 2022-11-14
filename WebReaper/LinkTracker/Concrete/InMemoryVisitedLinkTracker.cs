using System.Collections.Concurrent;
using System.Collections.Immutable;
using WebReaper.LinkTracker.Abstract;

namespace WebReaper.LinkTracker.Concrete;

public class InMemoryVisitedLinkTracker : IVisitedLinkTracker
{
    private ImmutableHashSet<string> visitedUrls = ImmutableHashSet.Create<string>();

    public Task AddVisitedLinkAsync(string visitedLink)
    {
        visitedUrls = visitedUrls.Add(visitedLink);

        return Task.CompletedTask;
    }

    public Task<List<string>> GetVisitedLinksAsync()
    {
        return Task.FromResult(visitedUrls.ToList());
    }

    public async Task<List<string>> GetNotVisitedLinks(IEnumerable<string> links)
    {
        return links.Except(visitedUrls).ToList();
    }

    public Task<long> GetVisitedLinksCount()
    {
        return Task.FromResult((long)visitedUrls.Count);
    }
}