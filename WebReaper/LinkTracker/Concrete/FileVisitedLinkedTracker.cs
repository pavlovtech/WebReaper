using System.Collections.Concurrent;
using WebReaper.LinkTracker.Abstract;

namespace WebReaper.LinkTracker.Concrete;

public class FileVisitedLinkedTracker : IVisitedLinkTracker
{
    private readonly string _fileName;
    private readonly ConcurrentBag<string> _visitedLinks;

    public FileVisitedLinkedTracker(string fileName)
    {
        _fileName = fileName;
        var allLinks = File.ReadLines(fileName);
        _visitedLinks = new ConcurrentBag<string>(allLinks);
    }
    
    public async Task AddVisitedLinkAsync(string siteId, string visitedLink)
    {
        _visitedLinks.Add(visitedLink);
        await File.AppendAllTextAsync(visitedLink, $"{siteId}.txt");
    }

    public Task<IEnumerable<string>> GetVisitedLinksAsync(string siteId)
    {
        return Task.FromResult(_visitedLinks.AsEnumerable());
    }

    public Task<IEnumerable<string>> GetNotVisitedLinks(string siteId, IEnumerable<string> links)
    {
        return Task.FromResult(links.Except(_visitedLinks));
    }

    public Task<long> GetVisitedLinksCount(string siteId)
    {
        return Task.FromResult((long)_visitedLinks.Count);
    }
}