using Microsoft.Extensions.Logging;
using System.Diagnostics;
using WebReaper.Domain;
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

    public async Task Run(int parallelismDegree, TimeSpan? scrapingTimeout, CancellationToken cancellationToken = default)
    {
        Stopwatch sw = Stopwatch.StartNew();

        await Scheduler.Schedule(new Job(
            Config.ParsingScheme!,
            Config.StartUrl!,
            Config.LinkPathSelectors,
            Config.StartPageType,
            Config.initialScript), cancellationToken);

        var options = new ParallelOptions { MaxDegreeOfParallelism = parallelismDegree, CancellationToken = cancellationToken };

        await Parallel.ForEachAsync(Scheduler.GetAll(cancellationToken), options, async (job, token) =>
        {
            try
            {
                if (scrapingTimeout != null && sw.Elapsed >= scrapingTimeout)
                {
                    return;
                }

                var newJobs = await Spider.CrawlAsync(job, token);
                await Scheduler.Schedule(newJobs, token);

                if (token.IsCancellationRequested)
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error occurred when scraping {url}", job.Url);

                // return job back to the queue
                await Scheduler.Schedule(job, token);
            }
        });
    }
}