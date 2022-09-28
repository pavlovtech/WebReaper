using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using WebReaper.Abstractions.JobQueue;
using WebReaper.Abstractions.Spider;
using WebReaper.Domain;

namespace WebReaper.Core.Scraper;

public class ScraperRunner
{
    public ScraperConfig Config { get; init; }
    public IJobQueueReader JobQueueReader { get; init; }
    public IJobQueueWriter JobQueueWriter { get; init; }
    public ISpider Spider { get; init; }
    public ILogger Logger { get; init; }

    public ScraperRunner(
        ScraperConfig config,
        IJobQueueReader jobQueueReader,
        IJobQueueWriter jobQueueWriter,
        ISpider spider,
        ILogger logger)
    {
        JobQueueReader = jobQueueReader;
        JobQueueWriter = jobQueueWriter;
        Config = config;
        Spider = spider;
        Logger = logger;
    }

    public async Task Run(int parallelismDegree)
    {
        await JobQueueWriter.WriteAsync(new Job(
            Config.ParsingScheme!,
            Config.BaseUrl,
            Config.StartUrl!,
            ImmutableQueue.Create(Config.LinkPathSelectors.ToArray()),
            DepthLevel: 0));

        var options = new ParallelOptions { MaxDegreeOfParallelism = parallelismDegree };
        await Parallel.ForEachAsync(JobQueueReader.ReadAsync(), options, async (job, token) =>
        {
            try
            {
                var newJobs = await Spider.CrawlAsync(job);
                await JobQueueWriter.WriteAsync(newJobs.ToArray());
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error occurred when scraping {url}", job.Url);

                // return job back to the queue
                await JobQueueWriter.WriteAsync(job);
            }
        });
    }

    public async Task Stop()
    {
        await JobQueueWriter.CompleteAddingAsync();
    }
}