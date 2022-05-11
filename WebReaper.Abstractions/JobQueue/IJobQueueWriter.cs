using WebReaper.Domain;
namespace WebReaper.Abstractions.JobQueue;

public interface IJobQueueWriter
{
    Task WriteAsync(params Job[] jobs);
    Task CompleteAddingAsync();
}