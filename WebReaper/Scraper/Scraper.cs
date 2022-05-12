using System.Net;
using WebReaper.Domain;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using WebReaper.Absctracts.Sinks;
using WebReaper.LinkTracker.Abstract;
using WebReaper.Abstractions.JobQueue;
using WebReaper.Queue;
using WebReaper.Sinks;
using WebReaper.Domain.Parsing;
using Microsoft.Extensions.Logging.Abstractions;
using WebReaper.Queue.InMemory;
using WebReaper.Domain.Selectors;

namespace WebReaper.Scraper;

public class Scraper
{
    protected ScraperConfigBuilder ConfigBuilder { get; set; } = new();
    protected SpiderBuilder SpiderBuilder { get; set; } = new();
    protected ScraperRunner Runner { get; set; }

    protected ILogger Logger { get; set; } = NullLogger.Instance;

    protected IJobQueueReader JobQueueReader;

    protected IJobQueueWriter JobQueueWriter;

    protected BlockingCollection<Job> jobs;

    public Scraper()
    {
        jobs = new(new ProducerConsumerPriorityQueue());   
        JobQueueReader = new JobQueueReader(jobs);
        JobQueueWriter = new JobQueueWriter(jobs);
    }

    public Scraper AddSink(IScraperSink sink)
    {
        SpiderBuilder.AddSink(sink);
        return this;
    }

    public Scraper Authorize(Func<CookieContainer> authorize)
    {
        SpiderBuilder.Authorize(authorize);
        return this;
    }

    public Scraper IgnoreUrls(params string[] urls)
    {
        SpiderBuilder.IgnoreUrls(urls);
        return this;
    }

    public Scraper Limit(int limit)
    {
        SpiderBuilder.Limit(limit);
        return this;
    }

    public Scraper WithLinkTracker(ICrawledLinkTracker linkTracker)
    {
        SpiderBuilder.WithLinkTracker(linkTracker);
        return this;
    }

    public Scraper WithLogger(ILogger logger)
    {
        SpiderBuilder.WithLogger(logger);
        ConfigBuilder.WithLogger(logger);

        this.Logger = logger;

        return this;
    }

    public Scraper WriteToConsole()
    {
        SpiderBuilder.WriteToConsole();
        return this;
    }
    
    public Scraper WriteToCosmosDb(
        string endpointUrl,
        string authorizationKey,
        string databaseId,
        string containerId)
    {
        SpiderBuilder.AddSink(new CosmosSink(endpointUrl, authorizationKey, databaseId, containerId, Logger));
        return this;
    }

    public Scraper WriteToCsvFile(string filePath)
    {
        SpiderBuilder.AddSink(new CsvFileSink(filePath));
        return this;
    }

    public Scraper WriteToJsonFile(string filePath)
    {
        SpiderBuilder.AddSink(new JsonFileSink(filePath));
        return this;
    }

    public Scraper Parse(Schema schema)
    {
        ConfigBuilder.WithScheme(schema);
        return this;
    }

    public Scraper WithStartUrl(string url)
    {
        ConfigBuilder.WithStartUrl(url);
        return this;
    }

    public Scraper FollowLinks(
        string linkSelector,
        SelectorType selectorType = SelectorType.Css,
        PageType pageType = PageType.Static)
    {
        ConfigBuilder.FollowLinks(linkSelector, selectorType, pageType);
        return this;
    }

    public Scraper FollowLinks(string linkSelector, string paginationSelector, SelectorType selectorType = SelectorType.Css, PageType pageType = PageType.Static)
    {
        ConfigBuilder.FollowLinks(linkSelector, paginationSelector, selectorType, pageType);
        return this;
    }

    public Scraper WithJobQueueWriter(IJobQueueWriter jobQueueWriter)
    {
        JobQueueWriter = jobQueueWriter;
        return this;
    }

    public Scraper WithJobQueueReader(IJobQueueReader jobQueueReader)
    {
        JobQueueReader = jobQueueReader;
        return this;
    }

    public async Task Run(int parallelismDegree)
    {
        var config = ConfigBuilder.Build();
        var spider = SpiderBuilder.Build();

        await JobQueueWriter.WriteAsync(new Job(
            config.ParsingScheme!,
            config.BaseUrl,
            config.StartUrl!,
            ImmutableQueue.Create(config.LinkPathSelectors.ToArray()),
            DepthLevel: 0));

        var options = new ParallelOptions { MaxDegreeOfParallelism = parallelismDegree };
        await Parallel.ForEachAsync(JobQueueReader.ReadAsync(), options, async (job, token) =>
        {
            try
            {
                var newJobs = await spider.CrawlAsync(job);
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
}
