using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using WebReaper.ConfigStorage.Abstract;
using WebReaper.Core.Scheduler.Abstract;
using WebReaper.Core.Spider.Abstract;
using WebReaper.Domain;
using WebReaper.Exceptions;
using static WebReaper.Infra.Executor;

namespace WebReaper.Core;

public class ScraperEngine
{
    public ScraperEngine(
        int parallelismDegree,
        IScraperConfigStorage configStorage,
        IScheduler jobScheduler,
        ISpider spider,
        ILogger logger)
    {
        ParallelismDegree = parallelismDegree;
        ArgumentNullException.ThrowIfNull(configStorage);
        ArgumentNullException.ThrowIfNull(jobScheduler);
        ArgumentNullException.ThrowIfNull(spider);
        ArgumentNullException.ThrowIfNull(logger);

        (Scheduler, Spider, Logger, ConfigStorage) =
            (jobScheduler, spider, logger, configStorage);
    }

    private IScraperConfigStorage ConfigStorage { get; }
    private IScheduler Scheduler { get; }
    private ISpider Spider { get; }
    private ILogger Logger { get; }
    
    private int ParallelismDegree { get; }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await Scheduler.Initialization;

        Logger.LogInformation("Start {class}.{method}", nameof(ScraperEngine), nameof(RunAsync));

        var config = await ConfigStorage.GetConfigAsync();

        foreach (var startUrl in config.StartUrls)
        {
            Logger.LogInformation("Scheduling the initial scraping job with start url {startUrl}", startUrl);

            await Scheduler.AddAsync(new Job(
                startUrl,
                config.LinkPathSelectors,
                ImmutableQueue.Create<string>(),
                config.StartPageType,
                config.PageActions), cancellationToken);
        }

        var options = new ParallelOptions { MaxDegreeOfParallelism = ParallelismDegree };

        try
        {
            Logger.LogInformation("Start consuming the scraping jobs");

            await Parallel.ForEachAsync(Scheduler.GetAllAsync(cancellationToken), options, async (job, token) =>
            {
                Logger.LogInformation("Start crawling url {Url}", job.Url);

                //var newJobs = await RetryAsync(async() => await Spider.CrawlAsync(job, cancellationToken));
                var newJobs = await RetryAsync(() => Spider.CrawlAsync(job, cancellationToken));

                Logger.LogInformation("Received {JobsCount} new jobs", newJobs.Count);

                await Scheduler.AddAsync(newJobs, cancellationToken);
            });
        }
        catch (PageCrawlLimitException ex)
        {
            Logger.LogWarning(ex, "Shutting down due to page crawl limit {Limit}", ex.PageCrawlLimit);
            throw;
        }
        catch (TaskCanceledException ex)
        {
            Logger.LogWarning(ex, "Shutting down due to cancellation");
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Shutting down due to unhandled exception");
            throw;
        }
    }
}