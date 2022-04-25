using WebReaper.Domain;
namespace WebReaper.Abstractions.JobQueue;

public interface IJobQueueWriter
{
    void Write(Job job);
    void CompleteAdding();
}