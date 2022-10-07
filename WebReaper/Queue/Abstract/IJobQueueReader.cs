using WebReaper.Domain;

namespace WebReaper.Queue.Abstract;

public interface IJobQueueReader
{
    IAsyncEnumerable<Job> ReadAsync();
}