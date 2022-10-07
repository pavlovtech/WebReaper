using System.Threading.Channels;
using WebReaper.Domain;
using WebReaper.Queue.Abstract;

namespace WebReaper.Queue.Concrete.InMemory;

public class JobQueueWriter : IJobQueueWriter
{
    private readonly ChannelWriter<Job> writer;

    public JobQueueWriter(ChannelWriter<Job> writer) => this.writer = writer;

    public async Task WriteAsync(params Job[] jobs)
    {
        foreach (Job job in jobs)
        {
            await writer.WriteAsync(job);
        }
    }

    public Task CompleteAddingAsync()
    {
        writer.Complete();
        return Task.CompletedTask;
    }
}