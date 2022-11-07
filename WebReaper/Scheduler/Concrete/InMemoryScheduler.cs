using System.Threading.Channels;
using WebReaper.Domain;
using WebReaper.Scheduler.Abstract;

namespace WebReaper.Scheduler.Concrete;

public class InMemoryScheduler : IScheduler
{
    private readonly Channel<Job> _jobChannel = Channel.CreateUnbounded<Job>();

    public async ValueTask<Job> GetAsync(CancellationToken cancellationToken)
    {
        return await _jobChannel.Reader.ReadAsync(cancellationToken);
    }

    public IAsyncEnumerable<Job> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return _jobChannel.Reader.ReadAllAsync(cancellationToken);
    }

    public async ValueTask AddAsync(Job job, CancellationToken cancellationToken = default)
    {
        await _jobChannel.Writer.WriteAsync(job, cancellationToken);
    }

    public async ValueTask AddAsync(IEnumerable<Job> jobs, CancellationToken cancellationToken = default)
    {
        foreach (var job in jobs)
        {
            await _jobChannel.Writer.WriteAsync(job, cancellationToken);
        }
    }
}