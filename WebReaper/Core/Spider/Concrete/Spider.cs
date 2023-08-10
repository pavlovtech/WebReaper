using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using WebReaper.ConfigStorage.Abstract;
using WebReaper.Core.LinkTracker.Abstract;
using WebReaper.Core.Loaders.Abstract;
using WebReaper.Core.Parser.Abstract;
using WebReaper.Core.Spider.Abstract;
using WebReaper.Domain;
using WebReaper.Domain.Selectors;
using WebReaper.Exceptions;
using WebReaper.Extensions;
using WebReaper.Sinks.Abstract;
using WebReaper.Sinks.Models;

namespace WebReaper.Core.Spider.Concrete;

public class Spider : ISpider
{
    public Spider(
        List<IScraperSink> sinks,
        ILinkParser linkParser,
        IContentParser contentParser,
        IVisitedLinkTracker linkTracker,
        IStaticPageLoader staticPageLoader,
        IBrowserPageLoader dynamicPageLoader,
        IScraperConfigStorage configStorage,
        ILogger logger)
    {
        Sinks = sinks;
        LinkParser = linkParser;
        ContentParser = contentParser;
        LinkTracker = linkTracker;
        StaticStaticPageLoader = staticPageLoader;
        BrowserPageLoader = dynamicPageLoader;
        ScraperConfigStorage = configStorage;
        Logger = logger;
    }

    private IStaticPageLoader StaticStaticPageLoader { get; }
    private IBrowserPageLoader BrowserPageLoader { get; }
    private ILinkParser LinkParser { get; }
    private IContentParser ContentParser { get; }

    private IVisitedLinkTracker LinkTracker { get; }

    private IScraperConfigStorage ScraperConfigStorage { get; }

    private List<IScraperSink> Sinks { get; }

    private ILogger Logger { get; }

    public async Task<List<Job>> CrawlAsync(Job job, CancellationToken cancellationToken = default)
    {
        await LinkTracker.Initialization;
        
        var config = await ScraperConfigStorage.GetConfigAsync();

        if (config.UrlBlackList.Contains(job.Url)) return Enumerable.Empty<Job>().ToList();

        if (await LinkTracker.GetVisitedLinksCount() >= config.PageCrawlLimit)
        {
            Logger.LogInformation("Page crawl limit has been reached");

            throw new PageCrawlLimitException("Page crawl limit has been reached.")
            {
                PageCrawlLimit = config.PageCrawlLimit
            };
        }

        await LinkTracker.AddVisitedLinkAsync(job.Url);

        var doc = job.PageType switch
        {
            PageType.Static => await LoadStaticPage(job),
            PageType.Dynamic => await LoadDynamicPage(job, config.Headless),
            _ => throw new NotImplementedException()
        };
        
        if (job.PageCategory == PageCategory.TargetPage)
        {
            await ProcessTargetPage(job, doc, cancellationToken);

            return Enumerable.Empty<Job>().ToList();
        }

        var newLinkPathSelectors = job.LinkPathSelectors.Dequeue(out var currentSelector);

        var baseUrl = new Uri(job.Url);

        Logger.LogDebug("Base url: {BaseUrl}", baseUrl);

        var rawLinks = await LinkParser.GetLinksAsync(baseUrl, doc, currentSelector.Selector);

        var links = rawLinks
            .Except(await LinkTracker.GetVisitedLinksAsync());

        var newJobs = new List<Job>();

        newJobs.AddRange(CreateNextJobs(job, currentSelector, newLinkPathSelectors, links, cancellationToken));

        if (job.PageCategory != PageCategory.PageWithPagination) return newJobs;

        var nextJobs = await CreateJobsForPagesWithPagination(job, currentSelector, baseUrl, doc, cancellationToken);

        newJobs.AddRange(nextJobs);

        return newJobs;
    }

    public event Action<ParsedData>? ScrapedData;

    public event Func<Metadata, JObject, Task>? PostProcessor;

    private async Task ProcessTargetPage(Job job, string doc, CancellationToken cancellationToken = default)
    {
        Logger.LogInvocationCount();

        var config = await ScraperConfigStorage.GetConfigAsync();

        var rowResult = await ContentParser.ParseAsync(doc, config.ParsingScheme);

        var result = new ParsedData(job.Url, rowResult);

        if (PostProcessor is not null)
            await PostProcessor.Invoke(new Metadata(job.ParentBacklinks.ToList(), job.Url, doc), result.Data);

        ScrapedData?.Invoke(result);

        Logger.LogInformation("Sending scraped data to sinks...");
        var sinkTasks = Sinks.Select(sink => sink.EmitAsync(result, cancellationToken));

        Logger.LogInformation("Waiting for sinks ...");
        await Task.WhenAll(sinkTasks);
        Logger.LogInformation("Finished waiting for sinks");
    }

    private async Task<string> LoadStaticPage(Job job)
    {
        Logger.LogInformation("Loading static page {Url}", job.Url);
        var doc = await StaticStaticPageLoader.Load(job.Url);

        return doc;
    }

    private async Task<string> LoadDynamicPage(Job job, bool headless)
    {
        Logger.LogInformation("Loading dynamic page {Url}", job.Url);
        var doc = await BrowserPageLoader.Load(job.Url, job.PageActions, headless);

        return doc;
    }

    private async Task<List<Job>> CreateJobsForPagesWithPagination(
        Job job,
        LinkPathSelector currentSelector,
        Uri baseUrl, string doc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(currentSelector.PaginationSelector);

        var rawPaginatedLinks = await LinkParser.GetLinksAsync(baseUrl, doc, currentSelector.PaginationSelector);

        Logger.LogInformation("Found {Pages} with pagination", rawPaginatedLinks.Count);

        if (!rawPaginatedLinks.Any())
            Logger.LogInformation("No pages with pagination found with selector {Selector} on {Url}",
                currentSelector.PaginationSelector, job.Url);

        var linksToPaginatedPages = await LinkTracker.GetNotVisitedLinks(rawPaginatedLinks);

        var nextJobs = CreateNextJobs(job, currentSelector, job.LinkPathSelectors, linksToPaginatedPages,
            cancellationToken);
        return nextJobs;
    }

    private List<Job> CreateNextJobs(
        Job job,
        LinkPathSelector currentSelector,
        ImmutableQueue<LinkPathSelector> selectors,
        IEnumerable<string> links,
        CancellationToken cancellationToken = default)
    {
        return links
            .TakeWhile(_ => !cancellationToken.IsCancellationRequested)
            .Select(link => job with
            {
                Url = link,
                LinkPathSelectors = selectors,
                ParentBacklinks = job.ParentBacklinks.Enqueue(job.Url),
                PageType = currentSelector.PageType,
                PageActions = currentSelector.PageActions
            })
            .ToList();
    }
}