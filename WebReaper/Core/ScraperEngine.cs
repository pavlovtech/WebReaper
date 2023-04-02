using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using WebReaper.ConfigStorage;
using WebReaper.ConfigStorage.Abstract;
using WebReaper.Core.Scheduler.Abstract;
using WebReaper.Core.Spider.Abstract;
using WebReaper.Domain;
using WebReaper.Exceptions;
using static WebReaper.Infra.Executor;

namespace WebReaper.Core;

public class ScraperEngine
{
    private IScraperConfigStorage ConfigStorage { get; }
    private ScraperConfig Config { get; }
    private IScheduler Scheduler { get; }
    private ISpider Spider { get; }
    private ILogger Logger { get; }

    public ScraperEngine(
        ScraperConfig config,
        IScraperConfigStorage configStorage,
        IScheduler jobScheduler,
        ISpider spider,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(configStorage);
        ArgumentNullException.ThrowIfNull(jobScheduler);
        ArgumentNullException.ThrowIfNull(spider);
        ArgumentNullException.ThrowIfNull(logger);

        (Scheduler, Config, Spider, Logger, ConfigStorage) =
            (jobScheduler, config, spider, logger, configStorage);
    }

    public async Task Run(int parallelismDegree = 8, CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("Start {class}.{method}", nameof(ScraperEngine), nameof(Run));

        var config = await ConfigStorage.GetConfigAsync();

        if (config == null)
        {
            await ConfigStorage.CreateConfigAsync(Config);
            config = await ConfigStorage.GetConfigAsync();
        }
        
        Logger.LogInformation("Scheduling the initial scraping job with start url {startUrl}", config.StartUrl);
        
        await Scheduler.AddAsync(new Job(
            Config.StartUrl!,
            Config.LinkPathSelectors,
            ImmutableQueue.Create<string>(),
            Config.StartPageType,
            Config.PageActions), cancellationToken);

        var options = new ParallelOptions { MaxDegreeOfParallelism = parallelismDegree };

        try
        {
            Logger.LogInformation("Start consuming the scraping jobs", config.StartUrl);
            
            await Parallel.ForEachAsync(Scheduler.GetAllAsync(cancellationToken), options, async (job, token) =>
            {
                Logger.LogInformation("Start crawling url {Url}", job.Url);
                
                var newJobs = await RetryAsync(() => Spider.CrawlAsync(job, cancellationToken));
                
                Logger.LogInformation("Received {JobsCount} new jobs", newJobs.Count);

                await Scheduler.AddAsync(newJobs, cancellationToken);
            });
        }
        catch (PageCrawlLimitException ex)
        {
            Logger.LogWarning(ex, "Shutting down due to page crawl limit {Limit}", ex.PageCrawlLimit);
        }
        catch (TaskCanceledException ex)
        {
            Logger.LogWarning(ex, "Shutting down due to cancellation");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Shutting down due to unhandled exception");
            throw;
        }
    }
}