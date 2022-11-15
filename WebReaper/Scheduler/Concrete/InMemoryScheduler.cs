using System.Threading.Channels;
using WebReaper.Domain;
using WebReaper.Scheduler.Abstract;

namespace WebReaper.Scheduler.Concrete;

public class InMemoryScheduler : IScheduler
{
    private readonly Channel<Job> _jobChannel = Channel.CreateUnbounded<Job>();
    
    public IAsyncEnumerable<Job> GetAllAsync(CancellationToken cancellationToken = default) =>
        _jobChannel.Reader.ReadAllAsync(cancellationToken);

    public async Task AddAsync(Job job, CancellationToken cancellationToken = default) =>
        await _jobChannel.Writer.WriteAsync(job, cancellationToken);

    public async Task AddAsync(IEnumerable<Job> jobs, CancellationToken cancellationToken = default)
    {
        foreach (var job in jobs)
            await _jobChannel.Writer.WriteAsync(job, cancellationToken);
    }
}