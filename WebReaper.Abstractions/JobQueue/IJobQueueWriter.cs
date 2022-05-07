using WebReaper.Domain;
namespace WebReaper.Abstractions.JobQueue;

public interface IJobQueueWriter
{
    Task WriteAsync(Job job);
    Task CompleteAddingAsync();
}