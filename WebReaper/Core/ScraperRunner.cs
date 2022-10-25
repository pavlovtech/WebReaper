using Microsoft.Extensions.Logging;
using WebReaper.Domain;
using WebReaper.Exceptions;
using WebReaper.Infra;
using WebReaper.Scheduler.Abstract;
using WebReaper.Spider.Abstract;

namespace WebReaper.Core;

public class ScraperRunner
{
    private ScraperConfig Config { get; }
    private string GlobalId { get; }
    private IScheduler Scheduler { get; }
    private ISpider Spider { get; }
    private ILogger Logger { get; }

    public ScraperRunner(
        string globalId,
        ScraperConfig config,
        IScheduler jobScheduler,
        ISpider spider,
        ILogger logger) => (GlobalId, Scheduler, Config, Spider, Logger) =
        (globalId, jobScheduler, config, spider, logger);

    public async Task Run(int parallelismDegree, CancellationToken cancellationToken = default)
    {
        await Scheduler.AddAsync(new Job(
            GlobalId,
            Config.ParsingScheme!,
            Config.StartUrl!,
            Config.LinkPathSelectors,
            Config.StartPageType,
            Config.Script), cancellationToken);

        var options = new ParallelOptions { MaxDegreeOfParallelism = parallelismDegree };

        try
        {
            await Parallel.ForEachAsync(Scheduler.GetAllAsync(cancellationToken), options, async (job, token) =>
            {
                try
                {
                    var newJobs = await Executor.RetryAsync(() => Spider.CrawlAsync(job, cancellationToken));

                    Logger.LogInformation("Recievd {number} of new jobs", newJobs.Count());
                    await Scheduler.AddAsync(newJobs, cancellationToken);
                    Logger.LogInformation("Schedules {number} new jobs", newJobs.Count());

                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Failed during scraping {job}", job.ToString());
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
        catch (Exception ex)
        {
            Logger.LogError(ex, "Shutting down due to unhandled exception");
            throw;
        }
    }
}