using System.Collections.Concurrent;
using WebReaper.LinkTracker.Abstract;

namespace WebReaper.LinkTracker.Concrete;

public class InMemoryCrawledLinkTracker : ICrawledLinkTracker
{
    protected ConcurrentDictionary<string, ConcurrentBag<string>> visitedUrlsPerSite = new();

    public Task AddVisitedLinkAsync(string siteUrl, string visitedLink)
    {
        var alreadyExists = visitedUrlsPerSite.TryGetValue(siteUrl, out var visitedSiteUrls);

        if (alreadyExists)
        {
            if (!visitedSiteUrls!.Contains(visitedLink))
            {
                visitedSiteUrls!.Add(visitedLink);
            }
        }
        else
        {
            visitedUrlsPerSite.TryAdd(siteUrl, new ConcurrentBag<string>
            {
                visitedLink
            });
        }

        return Task.CompletedTask;
    }

    public Task<IEnumerable<string>> GetVisitedLinksAsync(string siteUrl)
    {
        var successful = visitedUrlsPerSite.TryGetValue(siteUrl, out var result);

        var visited = successful ? result! : Enumerable.Empty<string>();

        return Task.FromResult(visited);
    }

    public async Task<IEnumerable<string>> GetNotVisitedLinks(string siteUrl, IEnumerable<string> links)
    {
        var visited = await GetVisitedLinksAsync(siteUrl);
        return links.Except(visited);
    }

    public Task<long> GetVisitedLinksCount(string siteUrl)
    {
        var successful = visitedUrlsPerSite.TryGetValue(siteUrl, out var result);

        if (!successful)
        {
            return Task.FromResult(0L);
        }

        return Task.FromResult((long)result!.Count);
    }
}