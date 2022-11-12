using WebReaper.Domain;

namespace WebReaper.Scheduler.Abstract;

public interface IScheduler
{
    ValueTask AddAsync(Job job, CancellationToken cancellationToken = default);
    ValueTask AddAsync(IEnumerable<Job> jobs, CancellationToken cancellationToken = default);
    IAsyncEnumerable<Job> GetAllAsync(CancellationToken cancellationToken = default);
}