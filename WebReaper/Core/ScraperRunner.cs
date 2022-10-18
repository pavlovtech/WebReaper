using Microsoft.Extensions.Logging;
using System.Diagnostics;
using WebReaper.Domain;
using WebReaper.Exceptions;
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
            Config.initialScript), cancellationToken);

        var options = new ParallelOptions { MaxDegreeOfParallelism = parallelismDegree, CancellationToken = cancellationToken };

        try
        {
            await Parallel.ForEachAsync(Scheduler.GetAllAsync(cancellationToken), options, async (job, token) =>
            {
                var newJobs = await Spider.CrawlAsync(job, cancellationToken);
                await Scheduler.AddAsync(newJobs, cancellationToken);
            });
        }
        catch (PageCrawlLimitException ex)
        {
            Logger.LogError(ex, "Shutting down due to page crawl limit {limit}", ex.PageCrawlLimit);
            return;
        }
        catch (TaskCanceledException ex)
        {
            Logger.LogError(ex, "Shutting down due to cancellation");
            return;
        }

    }
}