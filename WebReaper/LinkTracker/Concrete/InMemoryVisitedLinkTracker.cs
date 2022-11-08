using System.Collections.Concurrent;
using WebReaper.LinkTracker.Abstract;

namespace WebReaper.LinkTracker.Concrete;

public class InMemoryVisitedLinkTracker : IVisitedLinkTracker
{
    private readonly ConcurrentDictionary<string, ConcurrentBag<string>> visitedUrlsPerSite = new();

    public Task AddVisitedLinkAsync(string siteId, string visitedLink)
    {
        var alreadyExists = visitedUrlsPerSite.TryGetValue(siteId, out var visitedSiteUrls);

        if (alreadyExists)
        {
            if (!visitedSiteUrls!.Contains(visitedLink))
            {
                visitedSiteUrls!.Add(visitedLink);
            }
        }
        else
        {
            visitedUrlsPerSite.TryAdd(siteId, new ConcurrentBag<string>
            {
                visitedLink
            });
        }

        return Task.CompletedTask;
    }

    public Task<List<string>> GetVisitedLinksAsync(string siteId)
    {
        var successful = visitedUrlsPerSite.TryGetValue(siteId, out var result);

        var visited = successful ? result! : Enumerable.Empty<string>();

        return Task.FromResult(visited.ToList());
    }

    public async Task<List<string>> GetNotVisitedLinks(string siteId, IEnumerable<string> links)
    {
        var visited = await GetVisitedLinksAsync(siteId);
        return links.Except(visited).ToList();
    }

    public Task<long> GetVisitedLinksCount(string siteId)
    {
        var successful = visitedUrlsPerSite.TryGetValue(siteId, out var result);

        if (!successful)
        {
            return Task.FromResult(0L);
        }

        return Task.FromResult((long)result!.Count);
    }
}