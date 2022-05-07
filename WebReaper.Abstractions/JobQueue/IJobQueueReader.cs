using WebReaper.Domain;

namespace WebReaper.Abstractions.JobQueue;

public interface IJobQueueReader 
{
    IAsyncEnumerable<Job> ReadAsync();
}