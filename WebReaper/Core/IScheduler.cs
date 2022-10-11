using WebReaper.Domain;

namespace WebReaper.Core
{
    public interface IScheduler
    {
        void Enqueue(Job job);
        void Enqueue(IEnumerable<Job> jobs);
        Job Dequeue();
    }
}
