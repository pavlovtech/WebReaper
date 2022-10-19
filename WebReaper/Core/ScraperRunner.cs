using Microsoft.Extensions.Logging;
using WebReaper.Domain;
using WebReaper.Exceptions;
using WebReaper.Infra;
using WebReaper.Scheduler.Abstract;
using WebReaper.Spider.Abstract;

namespace WebReaper.Core;

public class ScraperRunner
{
    public ScraperConfig Config { get; init; }
    public IScheduler Scheduler { get; init; }
    public ISpider Spider { get; init; }
    public ILogger Logger { get; init; }
      
    public ScraperRunner(
        ScraperConfig config,
        IScheduler jobScheduler,
        ISpider spider,
        ILogger logger)
    {
        Scheduler = jobScheduler;
        Config = config;
        Spider = spider;
        Logger = logger;
    }

    public async Task Run(int parallelismDegree, CancellationToken cancellationToken = default)
    {
        await Scheduler.AddAsync(new Job(
            Config.ParsingScheme!,
            Config.StartUrl!,
            Config.LinkPathSelectors,
            Config.StartPageType,
            Config.Script), cancellationToken);

        var options = new ParallelOptions { MaxDegreeOfParallelism = parallelismDegree, CancellationToken = cancellationToken };

        try
        {
            await Parallel.ForEachAsync(Scheduler.GetAllAsync(cancellationToken), options, async (job, token) =>
            {
                try
                {
                    var newJobs = await Executor.RetryAsync(() => Spider.CrawlAsync(job, cancellationToken));
                    await Scheduler.AddAsync(newJobs, cancellationToken);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Falind during scraping {job}", job.ToString());
                }
            });
        }
        catch (PageCrawlLimitException ex)
        {
            Logger.LogWarning(ex, "Shutting down due to page crawl limit {limit}", ex.PageCrawlLimit);
            return;
        }
        catch (TaskCanceledException ex)
        {
            Logger.LogWarning(ex, "Shutting down due to cancellation");
            return;
        }
    }
}