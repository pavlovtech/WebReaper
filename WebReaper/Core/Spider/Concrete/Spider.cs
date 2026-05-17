using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using WebReaper.ConfigStorage.Abstract;
using WebReaper.Core.Crawling;
using WebReaper.Core.Crawling.Abstract;
using WebReaper.Core.LinkTracker.Abstract;
using WebReaper.Core.Loaders.Abstract;
using WebReaper.Core.Spider.Abstract;
using WebReaper.Domain;
using WebReaper.Exceptions;
using WebReaper.Extensions;
using WebReaper.Sinks.Abstract;
using WebReaper.Sinks.Models;

namespace WebReaper.Core.Spider.Concrete;

/// <summary>
/// The I/O shell around the <see cref="ICrawlStep"/>. It owns everything the
/// crawl step deliberately does not: page loading, the visited-link tracker,
/// the crawl-limit stop, sink fan-out, the PostProcessor / ScrapedData event,
/// and adapting the <see cref="CrawlOutcome"/> back to the engine's
/// <c>List&lt;Job&gt;</c> contract. The crawl-vs-parse decision itself lives
/// behind <see cref="ICrawlStep"/>.
/// </summary>
public class Spider : ISpider
{
    public Spider(
        List<IScraperSink> sinks,
        ICrawlStep crawlStep,
        IVisitedLinkTracker linkTracker,
        IPageLoader pageLoader,
        IScraperConfigStorage configStorage,
        ILogger logger)
    {
        Sinks = sinks;
        CrawlStep = crawlStep;
        LinkTracker = linkTracker;
        PageLoader = pageLoader;
        ScraperConfigStorage = configStorage;
        Logger = logger;
    }

    private IPageLoader PageLoader { get; }
    private ICrawlStep CrawlStep { get; }
    private IVisitedLinkTracker LinkTracker { get; }
    private IScraperConfigStorage ScraperConfigStorage { get; }
    private List<IScraperSink> Sinks { get; }
    private ILogger Logger { get; }

    public async Task<List<Job>> CrawlAsync(Job job, CancellationToken cancellationToken = default)
    {
        await LinkTracker.Initialization;

        var config = await ScraperConfigStorage.GetConfigAsync();

        if (config.UrlBlackList.Contains(job.Url)) return new List<Job>();

        await CheckCrawlLimit(config);

        await LinkTracker.AddVisitedLinkAsync(job.Url);

        var doc = await PageLoader.LoadAsync(
            new PageRequest(job.Url, job.PageType, job.PageActions, config.Headless),
            cancellationToken);

        var outcome = await CrawlStep.StepAsync(job, doc, config.ParsingScheme);

        if (outcome is CrawlOutcome.Parsed parsed)
        {
            await ProcessTargetPage(job, doc, parsed.Data, cancellationToken);

            await CheckCrawlLimit(config);

            return new List<Job>();
        }

        // Candidate Jobs come back unfiltered; the shell owns visited-link
        // de-duplication because it owns the tracker.
        var visited = (await LinkTracker.GetVisitedLinksAsync()).ToHashSet();

        return outcome.NextJobs
            .Where(nextJob => !visited.Contains(nextJob.Url))
            .ToList();
    }

    private async Task CheckCrawlLimit(ScraperConfig config)
    {
        if (await LinkTracker.GetVisitedLinksCount() >= config.PageCrawlLimit)
        {
            Logger.LogInformation("Page crawl limit has been reached");

            throw new PageCrawlLimitException("Page crawl limit has been reached.")
            {
                PageCrawlLimit = config.PageCrawlLimit
            };
        }
    }

    public event Action<ParsedData>? ScrapedData;

    public event Func<Metadata, JsonObject, Task>? PostProcessor;

    private async Task ProcessTargetPage(Job job, string doc, ParsedData result,
        CancellationToken cancellationToken = default)
    {
        Logger.LogInvocationCount();

        if (PostProcessor is not null)
            await PostProcessor.Invoke(new Metadata(job.ParentBacklinks.ToList(), job.Url, doc), result.Data);

        ScrapedData?.Invoke(result);

        Logger.LogInformation("Sending scraped data to sinks...");
        var sinkTasks = Sinks.Select(sink => sink.EmitAsync(result, cancellationToken));

        Logger.LogInformation("Waiting for sinks ...");
        await Task.WhenAll(sinkTasks);
        Logger.LogInformation("Finished waiting for sinks");
    }
}
