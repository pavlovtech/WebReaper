using System.Collections.Concurrent;
using WebReaper.Domain;

namespace WebReaper.Queue;

public class JobQueue : IJobQueue
{
    protected BlockingCollection<Job> jobs = new(new PubSubPriorityQueue());

    public void Add(Job job)
    {
        if(jobs.Any(existingJob => existingJob.Url == job.Url)) return;

        jobs.Add(job);
    }

    public void CompleteAdding() => jobs.CompleteAdding();

    public IEnumerable<Job> GetJobs() => jobs.GetConsumingEnumerable();
}