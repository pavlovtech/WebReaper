using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using WebReaper.Domain;
using WebReaper.Abstractions.Parsers;
using WebReaper.Domain.Selectors;
using WebReaper.Core.Extensions;
using WebReaper.Abstractions.Spider;
using WebReaper.Abstractions.Sinks;
using WebReaper.Abstractions.LinkTracker;
using WebReaper.Abstractions.Loaders;
using Newtonsoft.Json.Linq;

namespace WebReaper.Core.Spiders;

public class WebReaperSpider : ISpider
{
    public IPageLoader PageLoader { get; init; }
    public ILinkParser LinkParser { get; init; }
    public IContentParser ContentParser { get; init; }
    public ICrawledLinkTracker LinkTracker { get; init; }

    public List<string> UrlBlackList { get; set; } = new();

    public int PageCrawlLimit { get; set; } = int.MaxValue;

    public List<IScraperSink> Sinks { get; init; } = new();

    public event Action<JObject> ScrapedData;

    protected ILogger Logger { get; init; }

    public WebReaperSpider(
        List<IScraperSink> sinks,
        ILinkParser linkParser,
        IContentParser contentParser,
        ICrawledLinkTracker linkTracker,
        IPageLoader pageLoader,
        ILogger logger)
    {
        Sinks = sinks;
        LinkParser = linkParser;
        ContentParser = contentParser;
        LinkTracker = linkTracker;
        PageLoader = pageLoader;

        Logger = logger;
    }

    public async Task<IEnumerable<Job>> CrawlAsync(Job job)
    {
        if (UrlBlackList.Contains(job.Url)) return Enumerable.Empty<Job>();

        if (await LinkTracker.GetVisitedLinksCount(job.BaseUrl) >= PageCrawlLimit)
        {
            return Enumerable.Empty<Job>();
        }

        await LinkTracker.AddVisitedLinkAsync(job.BaseUrl, job.Url);

        string doc = await PageLoader.Load(job.Url);

        if (job.PageCategory == PageCategory.TargetPage)
        {
            Logger.LogInvocationCount("Handle on target page");
            var result = ContentParser.Parse(doc, job.schema);
            result.Add("URL", job.Url);

            ScrapedData(result);

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

            var allLinks = rawPaginatedLinks.Select(link => job.BaseUrl + link);

            var linksToPaginatedPages = await LinkTracker.GetNotVisitedLinks(job.BaseUrl, allLinks);

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