using WebReaper.Domain;

namespace WebReaper.Abstractions.JobQueue;

public interface IJobQueueReader 
{
    IEnumerable<Job> Read();

    int Count { get; }
}