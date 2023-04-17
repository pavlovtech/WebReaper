using WebReaper.Domain;

namespace WebReaper.Core.Scheduler.Abstract;

public interface IScheduler
{
    public bool DataCleanupOnStart { get; set; }
    
    Task AddAsync(Job job, CancellationToken cancellationToken = default);
    Task AddAsync(IEnumerable<Job> jobs, CancellationToken cancellationToken = default);
    IAsyncEnumerable<Job> GetAllAsync(CancellationToken cancellationToken = default);
}