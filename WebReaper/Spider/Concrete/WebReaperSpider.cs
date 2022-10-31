using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using WebReaper.Parser.Abstract;
using WebReaper.Domain.Selectors;
using WebReaper.Loaders.Abstract;
using WebReaper.Extensions;
using WebReaper.Domain;
using WebReaper.LinkTracker.Abstract;
using WebReaper.Sinks.Abstract;
using WebReaper.Spider.Abstract;
using WebReaper.Exceptions;

namespace WebReaper.Spider.Concrete;

public class WebReaperSpider : ISpider
{
    public IStaticPageLoader StaticStaticPageLoader { get; init; }
    public IBrowserPageLoader BrowserPageLoader { get; init; }
    public ILinkParser LinkParser { get; init; }
    public IContentParser ContentParser { get; init; }
    public ICrawledLinkTracker LinkTracker { get; init; }

    public List<string> UrlBlackList { get; set; } = new();

    public int PageCrawlLimit { get; set; } = int.MaxValue;

    public List<IScraperSink> Sinks { get; set; }

    public event Action<JObject>? ScrapedData;

    private ILogger Logger { get; }

    public WebReaperSpider(
        List<IScraperSink> sinks,
        ILinkParser linkParser,
        IContentParser contentParser,
        ICrawledLinkTracker linkTracker,
        IStaticPageLoader staticPageLoader,
        IBrowserPageLoader dynamicPageLoader,
        ILogger logger)
    {
        Sinks = sinks;
        LinkParser = linkParser;
        ContentParser = contentParser;
        LinkTracker = linkTracker;
        StaticStaticPageLoader = staticPageLoader;
        BrowserPageLoader = dynamicPageLoader;

        Logger = logger;
    }

    public async Task<List<Job>> CrawlAsync(Job job, CancellationToken cancellationToken = default)
    {
        if (UrlBlackList.Contains(job.Url)) return Enumerable.Empty<Job>().ToList();

        if (await LinkTracker.GetVisitedLinksCount(job.SiteId) >= PageCrawlLimit)
        {
            Logger.LogInformation("Page crawl limit has been reached.");

            throw new PageCrawlLimitException("Page crawl limit has been reached.") 
            {
                PageCrawlLimit = this.PageCrawlLimit
            };
        }

        await LinkTracker.AddVisitedLinkAsync(job.SiteId, job.Url);

        string doc = await LoadPage(job);

        if (job.PageCategory == PageCategory.TargetPage)
        {
            await ProcessTargetPage(job, cancellationToken, doc);

            return Enumerable.Empty<Job>().ToList();
        }

        var newLinkPathSelectors = job.LinkPathSelectors.Dequeue(out var currentSelector);

        var baseUrl = new Uri(job.Url);

        Logger.LogDebug("Base url: {BaseUrl}", baseUrl);

        var rawLinks = LinkParser.GetLinks(baseUrl, doc, currentSelector.Selector);

        var links = rawLinks
            .Except(await LinkTracker.GetVisitedLinksAsync(job.SiteId));

        var newJobs = new List<Job>();

        newJobs.AddRange(CreateNextJobs(job, currentSelector, newLinkPathSelectors, links, cancellationToken));

        if (job.PageCategory != PageCategory.PageWithPagination) return newJobs;
        
        var nextJobs = await CreateJobsForPagesWithPagination(job, currentSelector, baseUrl, doc, cancellationToken);

        newJobs.AddRange(nextJobs);

        return newJobs;
    }

    private async Task ProcessTargetPage(Job job, CancellationToken cancellationToken, string doc)
    {
        Logger.LogInvocationCount("Handle on target page");
        var result = ContentParser.Parse(doc, job.Schema);
        result.Add("URL", job.Url);

        ScrapedData?.Invoke(result);

        Logger.LogInformation("Sending scraped data to sinks...");
        var sinkTasks = Sinks.Select(sink => sink.EmitAsync(result, cancellationToken));

        Logger.LogInformation("Waiting for sinks ...");
        await Task.WhenAll(sinkTasks);
        Logger.LogInformation("Finished waiting for sinks");
    }

    private async Task<string> LoadPage(Job job)
    {
        string doc;
        if (job.PageType == PageType.Static)
        {
            Logger.LogInformation("Loading static page {URL}", job.Url);
            doc = await StaticStaticPageLoader.Load(job.Url);
        }
        else
        {
            Logger.LogInformation("Loading dynamic page {URL}", job.Url);
            doc = await BrowserPageLoader.Load(job.Url, job.Script);
        }

        return doc;
    }

    private async Task<List<Job>> CreateJobsForPagesWithPagination(Job job, LinkPathSelector currentSelector, Uri baseUrl, string doc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(currentSelector.PaginationSelector);

        var rawPaginatedLinks = LinkParser.GetLinks(baseUrl, doc, currentSelector.PaginationSelector);

        Logger.LogInformation("Found {Pages} with pagination", rawPaginatedLinks.Count);

        if (!rawPaginatedLinks.Any())
        {
            Logger.LogInformation("No pages with pagination found with selector {Selector} on {Url}",
                currentSelector.PaginationSelector, job.Url);
        }

        var linksToPaginatedPages = await LinkTracker.GetNotVisitedLinks(job.SiteId, rawPaginatedLinks);

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
            .TakeWhile(link => !cancellationToken.IsCancellationRequested)
            .Select(link => job with { Url = link, LinkPathSelectors = selectors, PageType = currentSelector.PageType, Script = currentSelector.ScriptExpression })
            .ToList();
    }
}