using System.Collections.Concurrent;
using WebReaper.Abstractions.JobQueue;
using WebReaper.Domain;

namespace WebReaper.Queue.InMemory;

public class JobQueueWriter : IJobQueueWriter
{
    private readonly BlockingCollection<Job> jobs;

    public JobQueueWriter(BlockingCollection<Job> jobs) => this.jobs = jobs;

    public Task WriteAsync(Job job)
    {
        if (jobs.Any(existingJob => existingJob.Url == job.Url)) return Task.CompletedTask;

        jobs.Add(job);

        return Task.CompletedTask;
    }

    public Task CompleteAddingAsync()
    {
        jobs.CompleteAdding();

        return Task.CompletedTask;
    }
}