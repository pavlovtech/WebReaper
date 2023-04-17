using System.Threading.Channels;
using WebReaper.Core.Scheduler.Abstract;
using WebReaper.Domain;

namespace WebReaper.Core.Scheduler.Concrete;

public class InMemoryScheduler : IScheduler
{
    private readonly Channel<Job> _jobChannel = Channel.CreateUnbounded<Job>();

    public IAsyncEnumerable<Job> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return _jobChannel.Reader.ReadAllAsync(cancellationToken);
    }

    public bool DataCleanupOnStart { get; set; }

    public Task Initialization { get; } = Task.CompletedTask;

    public async Task AddAsync(Job job, CancellationToken cancellationToken = default)
    {
        await _jobChannel.Writer.WriteAsync(job, cancellationToken);
    }

    public async Task AddAsync(IEnumerable<Job> jobs, CancellationToken cancellationToken = default)
    {
        foreach (var job in jobs)
            await _jobChannel.Writer.WriteAsync(job, cancellationToken);
    }
}