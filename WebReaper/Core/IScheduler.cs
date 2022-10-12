using WebReaper.Domain;

namespace WebReaper.Core
{
    public interface IScheduler
    {
        ValueTask Schedule(Job job);
        ValueTask Schedule(IEnumerable<Job> jobs);
        ValueTask<Job> Get();
        IAsyncEnumerable<Job> GetAll();
    }
}
