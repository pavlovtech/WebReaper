using System.Collections.Concurrent;
using WebReaper.LinkTracker.Abstract;

namespace WebReaper.LinkTracker.Concrete;

public class LinkTracker : ILinkTracker
{
    protected ConcurrentDictionary<string, ConcurrentBag<string>> visitedUrlsPerSite = new();

    public void AddVisitedLink(string siteUrl, string visitedLink)
    {
        var alreadyExists = visitedUrlsPerSite.TryGetValue(siteUrl, out var visitedSiteUrls);

        if(alreadyExists)
        {
            visitedSiteUrls!.Add(visitedLink);
        } else {
            visitedUrlsPerSite.TryAdd(siteUrl, new ConcurrentBag<string> 
            {
                visitedLink
            });
        }
    }

    public IEnumerable<string> GetVisitedLinks(string siteUrl)
    {
        var successful = visitedUrlsPerSite.TryGetValue(siteUrl, out var result);

        if(!successful)
        {
            return Enumerable.Empty<string>();
        }

        return result!;
    }
}