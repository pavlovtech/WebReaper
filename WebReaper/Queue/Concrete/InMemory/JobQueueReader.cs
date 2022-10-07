using System.Threading.Channels;
using WebReaper.Domain;
using WebReaper.Queue.Abstract;

namespace WebReaper.Queue.Concrete.InMemory;

public class JobQueueReader : IJobQueueReader
{
    private readonly ChannelReader<Job> reader;

    public JobQueueReader(ChannelReader<Job> reader) => this.reader = reader;

    public IAsyncEnumerable<Job> ReadAsync() => reader.ReadAllAsync();
}