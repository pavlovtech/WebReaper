using Microsoft.Extensions.Logging;
using WebReaper.Domain;
using WebReaper.Exceptions;
using WebReaper.Infra;
using WebReaper.Scheduler.Abstract;
using WebReaper.Spider.Abstract;

namespace WebReaper.Core;

public class ScraperEngine
{
    private ScraperConfig Config { get; }
    private string GlobalId { get; }
    private IScheduler Scheduler { get; }
    private ISpider Spider { get; }
    private ILogger Logger { get; }

    public ScraperEngine(
        string globalId,
        ScraperConfig config,
        IScheduler jobScheduler,
        ISpider spider,
        ILogger logger) => (GlobalId, Scheduler, Config, Spider, Logger) =
        (globalId, jobScheduler, config, spider, logger);

    public async Task Run(int parallelismDegree = 4, CancellationToken cancellationToken = default)
    {
        await Scheduler.AddAsync(new Job(
            GlobalId,
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
                var newJobs = await Executor.RetryAsync(() => Spider.CrawlAsync(job, cancellationToken));

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