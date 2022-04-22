using System.Collections.Concurrent;
using WebReaper.Domain;
using WebReaper.Queue.Abstract;

namespace WebReaper.Queue.Concrete;

public class JobQueue : IJobQueue
{
    protected BlockingCollection<Job> jobs = new(new ProducerConsumerPriorityQueue());

    public void Add(Job job)
    {
        if(jobs.Any(existingJob => existingJob.Url == job.Url)) return;

        jobs.Add(job);
    }

    public int Count => jobs.Count;

    public void CompleteAdding() => jobs.CompleteAdding();

    public IEnumerable<Job> Get() => jobs.GetConsumingEnumerable();
}