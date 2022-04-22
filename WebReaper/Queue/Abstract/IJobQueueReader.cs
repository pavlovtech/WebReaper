using WebReaper.Domain;

public interface IJobQueueReader 
{
    IEnumerable<Job> Read();

    int Count { get; }
}