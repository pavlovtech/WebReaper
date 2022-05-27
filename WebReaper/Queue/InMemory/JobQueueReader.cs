using System.Threading.Channels;
using WebReaper.Abstractions.JobQueue;
using WebReaper.Domain;

namespace WebReaper.Queue.InMemory;

public class JobQueueReader : IJobQueueReader
{
    private readonly ChannelReader<Job> reader;

    public JobQueueReader(ChannelReader<Job> reader) => this.reader = reader;

    public IAsyncEnumerable<Job> ReadAsync() => reader.ReadAllAsync();
}