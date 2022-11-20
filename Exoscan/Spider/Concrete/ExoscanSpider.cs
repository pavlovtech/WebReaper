using System.Collections.Immutable;
using Exoscan.Domain;
using Exoscan.Domain.Selectors;
using Exoscan.Exceptions;
using Exoscan.LinkTracker.Abstract;
using Exoscan.Loaders.Abstract;
using Exoscan.Parser.Abstract;
using Exoscan.Sinks.Abstract;
using Exoscan.Sinks.Models;
using Exoscan.Spider.Abstract;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Exoscan.Extensions;

namespace Exoscan.Spider.Concrete;

public class ExoscanSpider : ISpider
{
    public IStaticPageLoader StaticStaticPageLoader { get; init; }
    public IBrowserPageLoader BrowserPageLoader { get; init; }
    public ILinkParser LinkParser { get; init; }
    public IContentParser ContentParser { get; init; }
    public IVisitedLinkTracker LinkTracker { get; init; }

    public List<string> UrlBlackList { get; set; } = new();

    public int PageCrawlLimit { get; set; } = int.MaxValue;

    public List<IScraperSink> Sinks { get; set; }

    public event Action<ParsedData>? ScrapedData;

    private ILogger Logger { get; }

    public ExoscanSpider(
        List<IScraperSink> sinks,
        ILinkParser linkParser,
        IContentParser contentParser,
        IVisitedLinkTracker linkTracker,
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

        if (await LinkTracker.GetVisitedLinksCount() >= PageCrawlLimit)
        {
            Logger.LogInformation("Page crawl limit has been reached");

            throw new PageCrawlLimitException("Page crawl limit has been reached.") 
            {
                PageCrawlLimit = this.PageCrawlLimit
            };
        }

        await LinkTracker.AddVisitedLinkAsync(job.Url);

        string doc = await LoadPage(job);

        if (job.PageCategory == PageCategory.TargetPage)
        {
            await ProcessTargetPage(job, doc, cancellationToken);

            return Enumerable.Empty<Job>().ToList();
        }

        var newLinkPathSelectors = job.LinkPathSelectors.Dequeue(out var currentSelector);

        var baseUrl = new Uri(job.Url);

        Logger.LogDebug("Base url: {BaseUrl}", baseUrl);

        var rawLinks = LinkParser.GetLinks(baseUrl, doc, currentSelector.Selector);

        var links = rawLinks
            .Except(await LinkTracker.GetVisitedLinksAsync());

        var newJobs = new List<Job>();

        newJobs.AddRange(CreateNextJobs(job, currentSelector, newLinkPathSelectors, links, cancellationToken));

        if (job.PageCategory != PageCategory.PageWithPagination) return newJobs;
        
        var nextJobs = await CreateJobsForPagesWithPagination(job, currentSelector, baseUrl, doc, cancellationToken);

        newJobs.AddRange(nextJobs);

        return newJobs;
    }

    private async Task ProcessTargetPage(Job job, string doc, CancellationToken cancellationToken = default)
    {
        Logger.LogInvocationCount();
        var rowResult = ContentParser.Parse(doc, job.Schema);

        var result = new ParsedData(job.Url, rowResult);

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
            Logger.LogInformation("Loading static page {Url}", job.Url);
            doc = await StaticStaticPageLoader.Load(job.Url);
        }
        else
        {
            Logger.LogInformation("Loading dynamic page {Url}", job.Url);
            doc = await BrowserPageLoader.Load(job.Url, job.PageActions);
        }

        return doc;
    }

    private async Task<List<Job>> CreateJobsForPagesWithPagination(
        Job job,
        LinkPathSelector currentSelector,
        Uri baseUrl, string doc,
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
            .Select(link => job with { Url = link, LinkPathSelectors = selectors, PageType = currentSelector.PageType, PageActions = currentSelector.PageActions })
            .ToList();
    }
}