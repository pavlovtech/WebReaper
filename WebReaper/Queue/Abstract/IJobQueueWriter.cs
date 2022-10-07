using WebReaper.Core.Domain;

namespace WebReaper.Core.Queue.Abstract;

public interface IJobQueueWriter
{
    Task WriteAsync(params Job[] jobs);
    Task CompleteAddingAsync();
}