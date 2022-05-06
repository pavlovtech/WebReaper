using System.Collections.Concurrent;
using WebReaper.LinkTracker.Abstract;

namespace WebReaper.LinkTracker;

public class InMemoryLinkTracker : ILinkTracker
{
    protected ConcurrentDictionary<string, ConcurrentBag<string>> visitedUrlsPerSite = new();

    public Task AddVisitedLink(string siteUrl, string visitedLink)
    {
        var alreadyExists = visitedUrlsPerSite.TryGetValue(siteUrl, out var visitedSiteUrls);

        if(alreadyExists)
        {
            if(!visitedSiteUrls!.Contains(visitedLink)) {
                visitedSiteUrls!.Add(visitedLink);
            }
        } else {
            visitedUrlsPerSite.TryAdd(siteUrl, new ConcurrentBag<string> 
            {
                visitedLink
            });
        }

        return Task.CompletedTask;
    }

    public Task<IEnumerable<string>> GetVisitedLinks(string siteUrl)
    {
        var successful = visitedUrlsPerSite.TryGetValue(siteUrl, out var result);

        var visited = successful ? result! : Enumerable.Empty<string>();

        return Task.FromResult(visited);
    }
}