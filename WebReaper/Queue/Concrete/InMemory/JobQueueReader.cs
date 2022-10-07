using System.Threading.Channels;
using WebReaper.Core.Domain;
using WebReaper.Core.Queue.Abstract;

namespace WebReaper.Core.Queue.Concrete.InMemory;

public class JobQueueReader : IJobQueueReader
{
    private readonly ChannelReader<Job> reader;

    public JobQueueReader(ChannelReader<Job> reader) => this.reader = reader;

    public IAsyncEnumerable<Job> ReadAsync() => reader.ReadAllAsync();
}