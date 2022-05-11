using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using WebReaper.Domain;
using WebReaper.Extensions;
using WebReaper.LinkTracker.Abstract;
using WebReaper.Abastracts.Spider;
using WebReaper.Abstractions.Parsers;
using WebReaper.Absctracts.Sinks;
using WebReaper.Domain.Selectors;
using WebReaper.Abstractions.Loaders.PageLoader;

namespace WebReaper.Spiders;

public class WebReaperSpider : ISpider
{
    public IPageLoader StaticPageLoader { get; init; }
    public IPageLoader SpaPageLoader { get; init; }
    public ILinkParser LinkParser { get; init; }
    public IContentParser ContentParser { get; init; }
    public ICrawledLinkTracker LinkTracker { get; init; }

    public List<string> UrlBlackList { get; set; } = new();

    public int PageCrawlLimit { get; set; } = int.MaxValue;

    public List<IScraperSink> Sinks { get; init; } = new();

    protected ILogger Logger { get; init; }

    public WebReaperSpider(
        List<IScraperSink> sinks,
        ILinkParser linkParser,
        IContentParser contentParser,
        ICrawledLinkTracker linkTracker,
        IPageLoader staticPageLoader,
        IPageLoader spaPageLoader,
        ILogger logger)
    {
        Sinks = sinks;
        LinkParser = linkParser;
        ContentParser = contentParser;
        LinkTracker = linkTracker;
        StaticPageLoader = staticPageLoader;
        SpaPageLoader = spaPageLoader;

        Logger = logger;
    }

    public async Task<IEnumerable<Job>> CrawlAsync(Job job)
    {
        if (UrlBlackList.Contains(job.Url)) return Enumerable.Empty<Job>();

        if ((await LinkTracker.GetVisitedLinksAsync(job.BaseUrl)).Count() >= PageCrawlLimit)
        {
            return Enumerable.Empty<Job>();
        }

        await LinkTracker.AddVisitedLinkAsync(job.BaseUrl, job.Url);

        string doc;

        if (job.pageType == PageType.Static) 
        {
            doc = await StaticPageLoader.Load(job.Url);
        } else {
            doc = await SpaPageLoader.Load(job.Url);
        }

        if (job.PageCategory == PageCategory.TargetPage)
        {
            Logger.LogInvocationCount("Handle on target page");
            var result = ContentParser.Parse(doc, job.schema);

            var sinkTasks = Sinks.Select(sink => sink.EmitAsync(result));

            await Task.WhenAll(sinkTasks);
            return Enumerable.Empty<Job>();
        }

        var newLinkPathSelectors = job.LinkPathSelectors.Dequeue(out var currentSelector);

        var rawLinks = LinkParser.GetLinks(doc, currentSelector.Selector).ToList();

        var links = rawLinks
            .Select(link => job.BaseUrl + link)
            .Except(await LinkTracker.GetVisitedLinksAsync(job.BaseUrl));

        var newJobs = new List<Job>();

        newJobs.AddRange(CreateNextJobs(job, newLinkPathSelectors, links));

        if (job.PageCategory == PageCategory.PageWithPagination)
        {
            ArgumentNullException.ThrowIfNull(currentSelector.PaginationSelector);

            var rawPaginatedLinks = LinkParser.GetLinks(doc, currentSelector.PaginationSelector);

            if (!rawPaginatedLinks.Any())
            {
                Logger.LogInformation("No pages with pagination found with selector {selector} on {url}", currentSelector.PaginationSelector, job.Url);
            }

            var linksToPaginatedPages = rawPaginatedLinks
                .Select(link => job.BaseUrl + link)
                .Except(await LinkTracker.GetVisitedLinksAsync(job.BaseUrl));

            newJobs.AddRange(CreateNextJobs(job, job.LinkPathSelectors, linksToPaginatedPages));
        }

        return newJobs;
    }

    private IEnumerable<Job> CreateNextJobs(
        Job job,
        ImmutableQueue<LinkPathSelector> selectors,
        IEnumerable<string> links)
    {
        foreach (var link in links)
        {
            var newJob = new Job(job.schema, job.BaseUrl, link, selectors, job.DepthLevel + 1);
            yield return newJob;
        }
    }
}