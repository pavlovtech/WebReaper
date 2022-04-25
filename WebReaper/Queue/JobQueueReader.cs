using System.Collections.Concurrent;
using WebReaper.Abstractions.JobQueue;
using WebReaper.Domain;

namespace WebReaper.Queue;

public class JobQueueReader : IJobQueueReader
{
    private readonly BlockingCollection<Job> jobs;

    public JobQueueReader(BlockingCollection<Job> jobs)
    {
        this.jobs = jobs;
    }

    public int Count => jobs.Count;

    public IEnumerable<Job> Read()
    {
        return jobs.GetConsumingEnumerable();
    }
}