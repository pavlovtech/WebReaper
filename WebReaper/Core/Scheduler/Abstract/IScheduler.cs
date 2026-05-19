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
    /// Awaited once before the queue is used. A durable / distributed adapter
    /// performs its async connect / restore here; the in-memory adapter
    /// completes synchronously.
    /// </summary>
    public Task Initialization { get; }

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
    /// The async stream the Crawl driver drives. Yields Jobs as they arrive;
    /// it ends only once <see cref="Complete"/> has been signalled (the
    /// in-memory adapter) — a durable / distributed adapter keeps streaming by
    /// default, since it cannot know that no other worker will ever add more.
    /// </summary>
    IAsyncEnumerable<Job> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Signals that no more jobs will ever be added, so
    /// <see cref="GetAllAsync"/> can complete and the engine can stop
    /// once everything has been crawled (issue #20). Default is a no-op:
    /// durable / distributed schedulers keep their long-running
    /// behavior unless they choose to override this.
    /// </summary>
    void Complete() { }
}
