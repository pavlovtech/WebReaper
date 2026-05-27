using System.Collections.Concurrent;
using WebReaper.Core.LinkTracker.Abstract;
using WebReaper.DataAccess;
using WebReaper.Infra.Abstract;

namespace WebReaper.Core.LinkTracker.Concrete;

internal class FileVisitedLinkedTracker : IVisitedLinkTracker, IAsyncInitializable
{
    public bool DataCleanupOnStart { get; set; }
    private readonly Lazy<Task> _initialization;
    
    private readonly string _fileName;

    private readonly SemaphoreSlim _semaphore = new(1, 1);
    // Initialized to empty so the type-system contract holds from
    // construction. InitializeCoreAsync (driven once by InitializeAsync
    // before the crawl, ADR-0033) reassigns this from the file content;
    // the empty default is observable only if AddVisitedLinkAsync /
    // GetVisitedLinksAsync are called before InitializeAsync — a
    // sequencing error rather than a null deref.
    private ConcurrentBag<string> _visitedLinks = new();
    
    public FileVisitedLinkedTracker(string fileName, bool dataCleanupOnStart = false)
    {
        _fileName = fileName;
        DataCleanupOnStart = dataCleanupOnStart;
        
        _initialization = new Lazy<Task>(InitializeCoreAsync);
    }
    
    // ADR-0033: idempotent async warm-up, driven once before the crawl.
    public Task InitializeAsync() => _initialization.Value;

    private async Task InitializeCoreAsync()
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