using System.Collections.Concurrent;
using WebReaper.Core.LinkTracker.Abstract;

namespace WebReaper.Core.LinkTracker.Concrete;

public class FileVisitedLinkedTracker : IVisitedLinkTracker
{
    public bool DataCleanupOnStart { get; set; }
    public Task Initialization { get; }
    
    private readonly string _fileName;

    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private ConcurrentBag<string> _visitedLinks;
    
    public FileVisitedLinkedTracker(string fileName, bool dataCleanupOnStart = false)
    {
        _fileName = fileName;
        DataCleanupOnStart = dataCleanupOnStart;
        
        Initialization = InitializeAsync();
    }
    
    private async Task InitializeAsync()
    {
        if (DataCleanupOnStart)
        {
            if (File.Exists(_fileName))
            {
                File.Delete(_fileName);
            }
        }
        
        if (!File.Exists(_fileName))
        {
            var fileInfo = new FileInfo(_fileName);
            fileInfo.Directory?.Create();
            
            _visitedLinks = new ConcurrentBag<string>();
            var file = File.Create(_fileName);
            file.Close();
            return;
        }

        var allLinks = File.ReadLines(_fileName);
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