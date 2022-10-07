using WebReaper.Core.Domain;

namespace WebReaper.Core.Queue.Abstract;

public interface IJobQueueReader
{
    IAsyncEnumerable<Job> ReadAsync();
}