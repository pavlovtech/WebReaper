using WebReaper.Domain;

namespace WebReaper.Core.Scheduler.Abstract;

public interface IScheduler
{
    public bool DataCleanupOnStart { get; set; }
    public Task Initialization { get; }
    
    Task AddAsync(Job job, CancellationToken cancellationToken = default);
    Task AddAsync(IEnumerable<Job> jobs, CancellationToken cancellationToken = default);
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