using System.Collections.Concurrent;
using WebReaper.Domain;
using WebReaper.Queue.Abstract;

namespace WebReaper.Queue.Concrete;

public class JobQueueWriter : IJobQueueWriter
{
    private readonly BlockingCollection<Job> jobs;

    public JobQueueWriter(BlockingCollection<Job> jobs)
    {
        this.jobs = jobs;
    }

    public void Write(Job job)
    {
        if (jobs.Any(existingJob => existingJob.Url == job.Url)) return;

        jobs.Add(job);
    }

    public int Count => jobs.Count;

    public void CompleteAdding() => jobs.CompleteAdding();
}