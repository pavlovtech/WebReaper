using System.Collections.Concurrent;
using WebReaper.Core.LinkTracker.Abstract;
using WebReaper.DataAccess;

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
        // ADR-0011: directory creation, cleanup-on-start and the missing-file
        // policy are delegated to FilePersistencePrep — eager and
        // unconditional, fixing the old "directory created only when the file
        // is absent" bug. The in-memory mirror is this adapter's own essence.
        FilePersistencePrep.CleanupOnStart(_fileName, DataCleanupOnStart);
        FilePersistencePrep.EnsureDirectory(_fileName);

        var content = await FilePersistencePrep.ReadAllTextOrNullAsync(_fileName);
        if (content is null)
        {
            _visitedLinks = new ConcurrentBag<string>();
            File.Create(_fileName).Close();
            return;
        }

        _visitedLinks = new ConcurrentBag<string>(
            content.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
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