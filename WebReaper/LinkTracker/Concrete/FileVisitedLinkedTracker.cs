using System.Collections.Concurrent;
using WebReaper.LinkTracker.Abstract;

namespace WebReaper.LinkTracker.Concrete;

public class FileVisitedLinkedTracker : IVisitedLinkTracker
{
    private readonly string _fileName;
    private readonly ConcurrentBag<string> _visitedLinks;
    
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public FileVisitedLinkedTracker(string fileName)
    {
        _fileName = fileName;

        if (!File.Exists(fileName))
        {
            _visitedLinks = new ConcurrentBag<string>();
            var file = File.Create(fileName);
            file.Close();
            return;
        }
        
        var allLinks = File.ReadLines(fileName);
        _visitedLinks = new ConcurrentBag<string>(allLinks);
    }
    
    public async Task AddVisitedLinkAsync(string siteId, string visitedLink)
    {
        _visitedLinks.Add(visitedLink);
        
        await _semaphore.WaitAsync();
        try
        {
            await File.AppendAllTextAsync(_fileName, visitedLink + Environment.NewLine);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public Task<List<string>> GetVisitedLinksAsync(string siteId) => Task.FromResult(_visitedLinks.ToList());

    public Task<List<string>> GetNotVisitedLinks(string siteId, IEnumerable<string> links) =>
        Task.FromResult(links.Except(_visitedLinks).ToList());

    public Task<long> GetVisitedLinksCount(string siteId) => Task.FromResult((long)_visitedLinks.Count);
}