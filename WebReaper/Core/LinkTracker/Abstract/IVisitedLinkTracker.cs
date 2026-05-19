namespace WebReaper.Core.LinkTracker.Abstract;

public interface IVisitedLinkTracker
{
    public bool DataCleanupOnStart { get; set; }
    
    Task AddVisitedLinkAsync(string visitedLink);

    /// <summary>
    /// Atomic test-and-set: record <paramref name="visitedLink"/> and return
    /// <c>true</c> iff it was newly added (not already present). This is the
    /// single idempotency authority the Crawl driver gates on (ADR-0022): a
    /// duplicate discovery — or, once distributed, a redelivered Job — loses
    /// the race and becomes a no-op, so discovery dedup and Outstanding-work-
    /// latch accounting stay correct without relying on at-least-once queue
    /// semantics. The default is the behaviour-preserving non-atomic
    /// check-then-add (it has a race window) so existing adapters compile
    /// unchanged; an adapter that can do this atomically overrides it
    /// (InMemory here; the distributed adapters are ADR-0022 slice 3). The
    /// default-interface-method shape mirrors IScheduler.Complete().
    /// </summary>
    async Task<bool> TryAddVisitedLinkAsync(string visitedLink)
    {
        var visited = await GetVisitedLinksAsync();
        if (visited.Contains(visitedLink)) return false;
        await AddVisitedLinkAsync(visitedLink);
        return true;
    }

    Task<List<string>> GetVisitedLinksAsync();
    Task<List<string>> GetNotVisitedLinks(IEnumerable<string> links);
    Task<long> GetVisitedLinksCount();
    
    Task Initialization { get; }
}