using System.Collections.Concurrent;
using WebReaper.Core.LinkTracker.Abstract;

namespace WebReaper.Core.LinkTracker.Concrete;

public class FileVisitedLinkedTracker : IVisitedLinkTracker
{
    private readonly string _fileName;

    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly ConcurrentBag<string> _visitedLinks;

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

    public async Task AddVisitedLinkAsync(string visitedLink)
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

    public Task<List<string>> GetVisitedLinksAsync()
    {
        return Task.FromResult(_visitedLinks.ToList());
    }

    public Task<List<string>> GetNotVisitedLinks(IEnumerable<string> links)
    {
        return Task.FromResult(links.Except(_visitedLinks).ToList());
    }

    public Task<long> GetVisitedLinksCount()
    {
        return Task.FromResult((long)_visitedLinks.Count);
    }
}