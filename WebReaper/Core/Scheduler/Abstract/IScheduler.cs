using WebReaper.Domain;

namespace WebReaper.Core.Scheduler.Abstract;

/// <summary>
/// The job-queue seam: what holds <see cref="Job"/>s waiting to be crawled and
/// streams them to the Crawl driver (ADR-0022). Swapping this is how a crawl
/// moves from in-process (the in-memory default) to durable / distributed
/// state shared across workers (File, Redis, Azure Service Bus). The driver
/// pushes surviving child Jobs back through <see cref="AddAsync(Job, CancellationToken)"/>
/// and consumes <see cref="GetAllAsync"/> with <c>Parallel.ForEachAsync</c>.
/// </summary>
public interface IScheduler
{
    /// <summary>
    /// Wipe the backing store when the crawl starts (a fresh run) instead of
    /// resuming from it. No effect on the in-memory adapter; the durable
    /// adapters read it to decide fresh-vs-resume.
    /// </summary>
    public bool DataCleanupOnStart { get; set; }

    /// <summary>
    /// Enqueue one discovered Job. The Crawl driver calls this to push a
    /// surviving child Job back onto the queue.
    /// </summary>
    Task AddAsync(Job job, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enqueue a batch of Jobs in one call — the start URLs at seed time, or a
    /// page's discovered children together.
    /// </summary>
    Task AddAsync(IEnumerable<Job> jobs, CancellationToken cancellationToken = default);

    /// <summary>
    /// The async stream the Crawl driver drives with <c>Parallel.ForEachAsync</c>.
    /// Yields Jobs as they arrive; it ends when <paramref name="cancellationToken"/>
    /// is cancelled — the in-process Crawl driver cancels it once the stop rule
    /// concludes the Crawl (ADR-0037) — or, for a finite source, when the source
    /// is exhausted. Every adapter MUST observe the token <em>promptly</em>: a
    /// wait for the next Job (a poll delay, a blocking receive) must be
    /// cancellable, so a Crawl terminates the same way for every adapter, not
    /// only the in-memory one.
    /// </summary>
    IAsyncEnumerable<Job> GetAllAsync(CancellationToken cancellationToken = default);
}
