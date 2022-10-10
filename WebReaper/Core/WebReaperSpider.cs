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

namespace WebReaper.Core;

public class WebReaperSpider : ISpider
{
    public IStaticPageLoader StaticStaticPageLoader { get; init; }
    public IDynamicPageLoader DynamicPageLoader { get; init; }
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
        IStaticPageLoader staticPageLoader,
        IDynamicPageLoader dynamicPageLoader,
        ILogger logger)
    {
        Sinks = sinks;
        LinkParser = linkParser;
        ContentParser = contentParser;
        LinkTracker = linkTracker;
        StaticStaticPageLoader = staticPageLoader;
        DynamicPageLoader = dynamicPageLoader;

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

        string doc;

        if (job.PageType == PageType.Static)
        {
            doc = await StaticStaticPageLoader.Load(job.Url);
        }
        else
        {
            doc = await DynamicPageLoader.Load(job.Url, job.Script);
        }

        if (job.PageCategory == PageCategory.TargetPage)
        {
            Logger.LogInvocationCount("Handle on target page");
            var result = ContentParser.Parse(doc, job.Schema);
            result.Add("URL", job.Url);

            ScrapedData(result);

            var sinkTasks = Sinks.Select(sink => sink.EmitAsync(result));

            await Task.WhenAll(sinkTasks);
            return Enumerable.Empty<Job>();
        }

        var newLinkPathSelectors = job.LinkPathSelectors.Dequeue(out var currentSelector);

        var rawLinks = LinkParser.GetLinks(doc, currentSelector.Selector).ToList();

        var links = rawLinks
            .Select(link => new Uri(new Uri(job.BaseUrl), link).ToString())
            .Except(await LinkTracker.GetVisitedLinksAsync(job.BaseUrl));

        var newJobs = new List<Job>();

        newJobs.AddRange(CreateNextJobs(job, currentSelector, newLinkPathSelectors, links));

        if (job.PageCategory == PageCategory.PageWithPagination)
        {
            ArgumentNullException.ThrowIfNull(currentSelector.PaginationSelector);

            var rawPaginatedLinks = LinkParser.GetLinks(doc, currentSelector.PaginationSelector);

            if (!rawPaginatedLinks.Any())
            {
                Logger.LogInformation("No pages with pagination found with selector {selector} on {url}", currentSelector.PaginationSelector, job.Url);
            }

            var allLinks = rawPaginatedLinks.Select(link => new Uri(new Uri(job.BaseUrl), link).ToString());

            var linksToPaginatedPages = await LinkTracker.GetNotVisitedLinks(job.BaseUrl, allLinks);

            newJobs.AddRange(CreateNextJobs(job, currentSelector, job.LinkPathSelectors, linksToPaginatedPages));
        }

        return newJobs;
    }

    private IEnumerable<Job> CreateNextJobs(
        Job job,
        LinkPathSelector currentSelector,
        ImmutableQueue<LinkPathSelector> selectors,
        IEnumerable<string> links)
    {
        foreach (var link in links)
        {
            var newJob = job with { Url = link, LinkPathSelectors = selectors, PageType = currentSelector.PageType, Script = currentSelector.ScriptExpression };
            yield return newJob;
        }
    }
}