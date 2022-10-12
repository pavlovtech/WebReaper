using System.Threading.Channels;
using WebReaper.Domain;
using WebReaper.LinkTracker.Abstract;
using WebReaper.Scheduler.Abstract;

namespace WebReaper.Scheduler.Concrete;

public class InMemoryScheduler : IScheduler
{
    private readonly Channel<Job> JobChannel = Channel.CreateUnbounded<Job>();

    private readonly ICrawledLinkTracker LinkTracker;

    public List<string> UrlBlackList { get; set; } = new();
    public long Limit { get; set; } = long.MaxValue;

    public InMemoryScheduler(ICrawledLinkTracker linkTracker)
    {
        LinkTracker = linkTracker;
    }

    public async ValueTask<Job> Get()
    {
        return await JobChannel.Reader.ReadAsync();
    }

    public IAsyncEnumerable<Job> GetAll()
    {
        return JobChannel.Reader.ReadAllAsync();
    }

    public async ValueTask Schedule(Job job)
    {
        if (UrlBlackList.Contains(job.Url)) return;

        if (await LinkTracker.GetVisitedLinksCount(new Uri(job.Url).Host) >= Limit)
        {
            JobChannel.Writer.Complete();
        }

        await LinkTracker.AddVisitedLinkAsync(new Uri(job.Url).Host, job.Url);

        await JobChannel.Writer.WriteAsync(job);
    }

    public async ValueTask Schedule(IEnumerable<Job> jobs)
    {
        foreach (var job in jobs)
        {
            await Schedule(job);
        }
    }
}