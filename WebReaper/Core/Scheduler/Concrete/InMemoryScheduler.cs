using System.Threading.Channels;
using WebReaper.Core.Scheduler.Abstract;
using WebReaper.Domain;

namespace WebReaper.Core.Scheduler.Concrete;

/// <summary>
/// The default <see cref="IScheduler"/>: an in-process unbounded
/// <see cref="Channel{T}"/> of Jobs. Single-process only (the queue does not
/// survive a restart and is not shared across workers — use a durable /
/// distributed scheduler for that). Also the in-memory building block the
/// ADR-0009 DIY-distributed pattern can wire by hand.
/// </summary>
public class InMemoryScheduler : IScheduler
{
    private readonly Channel<Job> _jobChannel = Channel.CreateUnbounded<Job>();

    /// <inheritdoc/>
    // ADR-0037: the channel is intentionally never completed — the stream
    // ends when the Crawl driver cancels the token, the same mechanism every
    // durable scheduler uses. Termination is the driver ceasing consumption.
    public IAsyncEnumerable<Job> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return _jobChannel.Reader.ReadAllAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public bool DataCleanupOnStart { get; set; }

    /// <inheritdoc/>
    public async Task AddAsync(Job job, CancellationToken cancellationToken = default)
    {
        await _jobChannel.Writer.WriteAsync(job, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task AddAsync(IEnumerable<Job> jobs, CancellationToken cancellationToken = default)
    {
        foreach (var job in jobs)
            await _jobChannel.Writer.WriteAsync(job, cancellationToken);
    }
}
