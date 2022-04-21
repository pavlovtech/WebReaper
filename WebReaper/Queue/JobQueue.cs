using System.Collections.Concurrent;
using WebReaper.Domain;

namespace WebReaper.Queue;

public class JobQueue : IJobQueue
{
    protected BlockingCollection<Job> jobs = new(new ProducerConsumerQueue());

    public void Add(Job job)
    {
        if(jobs.Any(existingJob => existingJob.Url == job.Url)) return;

        jobs.Add(job);
    }

    public int Count => jobs.Count;

    public void CompleteAdding() => jobs.CompleteAdding();

    public IEnumerable<Job> Get() => jobs.GetConsumingEnumerable();
}