using Microsoft.Extensions.Logging;
using WebReaper.Domain;
using WebReaper.Exceptions;
using WebReaper.Scheduler.Abstract;
using WebReaper.Spider.Abstract;
using static WebReaper.Infra.Executor;

namespace WebReaper.Core;

public class ScraperEngine
{
    private ScraperConfig Config { get; }
    private IScheduler Scheduler { get; }
    private ISpider Spider { get; }
    private ILogger Logger { get; }

    public ScraperEngine(
        ScraperConfig config,
        IScheduler jobScheduler,
        ISpider spider,
        ILogger logger) => (Scheduler, Config, Spider, Logger) =
        (jobScheduler, config, spider, logger);

    public async Task Run(int parallelismDegree = 8, CancellationToken cancellationToken = default)
    {
        Logger.LogInformation($"Start {nameof(ScraperEngine)}.{nameof(Run)}");
        
        await Scheduler.AddAsync(new Job(
            Config.ParsingScheme!,
            Config.StartUrl!,
            Config.LinkPathSelectors,
            Config.StartPageType,
            Config.PageActions), cancellationToken);

        var options = new ParallelOptions { MaxDegreeOfParallelism = parallelismDegree };

        try
        {
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