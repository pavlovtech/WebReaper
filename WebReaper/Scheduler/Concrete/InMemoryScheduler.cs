using System.Threading.Channels;
using WebReaper.Domain;
using WebReaper.Scheduler.Abstract;

namespace WebReaper.Scheduler.Concrete;

public class InMemoryScheduler : IScheduler
{
    private readonly Channel<Job> JobChannel = Channel.CreateUnbounded<Job>();

    public async ValueTask<Job> Get()
    {
        return await JobChannel.Reader.ReadAsync();
    }

    public IAsyncEnumerable<Job> GetAll()
    {
        return JobChannel.Reader.ReadAllAsync();
    }

    public async ValueTask Schedule(Job job)
    {
        await JobChannel.Writer.WriteAsync(job);
    }

    public async ValueTask Schedule(IEnumerable<Job> jobs)
    {
        foreach (var job in jobs)
        {
            await JobChannel.Writer.WriteAsync(job);
        }
    }
}