using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using WebReaper.Abastracts.Spider;
using WebReaper.Domain;

namespace WebReaper.Scraper;

public class ScraperRunner
{
    public ScraperRunner(ScraperConfig config, ISpider spider, ILogger logger)
    {
        Config = config;
        Spider = spider;
        Logger = logger;
    }

    public async Task Run(int parallelismDegree)
    {
        await Spider.JobQueueWriter.WriteAsync(new Job(
            Config.ParsingScheme!,
            Config.StartUrl!,
            Config.BaseUrl,
            ImmutableQueue.Create(Config.LinkPathSelectors.ToArray()),
            DepthLevel: 0));

        var options = new ParallelOptions { MaxDegreeOfParallelism = parallelismDegree };
        await Parallel.ForEachAsync(Spider.JobQueueReader.ReadAsync(), options, async (job, token) =>
        {
            try
            {
                await Spider.CrawlAsync(job);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error occurred when scraping {url}", job.Url);

                // return job back to the queue
                await Spider.JobQueueWriter.WriteAsync(job);
            }
        });
    }

    protected ScraperConfig Config { get; init; }
    protected ISpider Spider { get; init; }
    public ILogger Logger { get; }
}