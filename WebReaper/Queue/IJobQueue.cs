using WebReaper.Domain;

namespace WebReaper.Queue;

public interface IJobQueue
{
    void Add(Job job);
    void CompleteAdding();
    IEnumerable<Job> Get();

    int Count { get; }
}
