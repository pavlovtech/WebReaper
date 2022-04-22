using WebReaper.Domain;

namespace WebReaper.Queue.Abstract
{
    public interface IJobQueueWriter
    {
        void Write(Job job);
        void CompleteAdding();
    }
}