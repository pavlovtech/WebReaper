using WebReaper.Domain;

namespace WebReaper.Queue.Abstract;

public interface IJobQueueWriter
{
    Task WriteAsync(params Job[] jobs);
    Task CompleteAddingAsync();
}