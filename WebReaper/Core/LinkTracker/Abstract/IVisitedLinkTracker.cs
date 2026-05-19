namespace WebReaper.Core.LinkTracker.Abstract;

/// <summary>
/// Crawl-global "what has this Crawl already visited" state, owned by the
/// Crawl driver (ADR-0022). It is the single idempotency authority: the atomic
/// test-and-set <see cref="TryAddVisitedLinkAsync"/> gates discovery dedup,
/// the crawl-page limit, and Outstanding-work-latch accounting in one
/// membership check. In-memory by default; the File / Redis adapters share it
/// across a resumed or distributed crawl.
/// </summary>
public interface IVisitedLinkTracker
{
    /// <summary>
    /// Wipe the visited set when the crawl starts (a fresh run) instead of
    /// resuming from it. No effect in-memory; the durable adapters read it.
    /// </summary>
    public bool DataCleanupOnStart { get; set; }

    /// <summary>
    /// Unconditionally record a visited link. Prefer
    /// <see cref="TryAddVisitedLinkAsync"/> for the dedup / accounting gate —
    /// that is the atomic test-and-set; this is the plain mark with no race
    /// verdict.
    /// </summary>
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

    /// <summary>
    /// A snapshot of the visited set. Backs the non-atomic default
    /// <see cref="TryAddVisitedLinkAsync"/>; an adapter with a native atomic
    /// add does not need it on the hot path.
    /// </summary>
    Task<List<string>> GetVisitedLinksAsync();

    /// <summary>
    /// The subset of <paramref name="links"/> not yet visited — a
    /// discovery-filtering helper.
    /// </summary>
    Task<List<string>> GetNotVisitedLinks(IEnumerable<string> links);

    /// <summary>
    /// The visited count the soft crawl-page-limit stop reads (ADR-0022: the
    /// limit is a value the driver checks, not a thrown exception).
    /// </summary>
    Task<long> GetVisitedLinksCount();

    /// <summary>
    /// Awaited once before use; durable / distributed adapters connect /
    /// restore here.
    /// </summary>
    Task Initialization { get; }
}
