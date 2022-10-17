using WebReaper.Domain;

namespace WebReaper.Scheduler.Abstract
{
    public interface IScheduler
    {
        ValueTask Schedule(Job job, CancellationToken cancellationToken = default);
        ValueTask Schedule(IEnumerable<Job> jobs, CancellationToken cancellationToken = default);
        ValueTask<Job> Get(CancellationToken cancellationToken = default);
        IAsyncEnumerable<Job> GetAll(CancellationToken cancellationToken = default);
    }
}
