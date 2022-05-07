using System.Collections.Concurrent;
using WebReaper.Abstractions.JobQueue;
using WebReaper.Domain;

namespace WebReaper.Queue.InMemory;

public class JobQueueReader : IJobQueueReader
{
    private readonly BlockingCollection<Job> jobs;

    public JobQueueReader(BlockingCollection<Job> jobs) => this.jobs = jobs;

    async IAsyncEnumerable<Job> IJobQueueReader.ReadAsync()
    {
        foreach (Job job in jobs.GetConsumingEnumerable())
        {
            yield return job;
        }
    }
}