using Exoscan.Domain;

namespace Exoscan.Scheduler.Abstract;

public interface IScheduler
{
    Task AddAsync(Job job, CancellationToken cancellationToken = default);
    Task AddAsync(IEnumerable<Job> jobs, CancellationToken cancellationToken = default);
    IAsyncEnumerable<Job> GetAllAsync(CancellationToken cancellationToken = default);
}