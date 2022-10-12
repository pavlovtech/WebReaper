using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using WebReaper.Parser.Abstract;
using WebReaper.Domain.Selectors;
using WebReaper.Loaders.Abstract;
using WebReaper.Extensions;
using WebReaper.Domain;
using WebReaper.Sinks.Abstract;
using WebReaper.Spider.Abstract;

namespace WebReaper.Spider.Concrete;

public class WebReaperSpider : ISpider
{
    public IStaticPageLoader StaticStaticPageLoader { get; init; }
    public IDynamicPageLoader DynamicPageLoader { get; init; }
    public ILinkParser LinkParser { get; init; }
    public IContentParser ContentParser { get; init; }

    public int Limit { get; set; } = int.MaxValue;

    public List<IScraperSink> Sinks { get; init; } = new();

    public event Action<JObject> ScrapedData;

    protected ILogger Logger { get; init; }

    public WebReaperSpider(
        List<IScraperSink> sinks,
        ILinkParser linkParser,
        IContentParser contentParser,
        IStaticPageLoader staticPageLoader,
        IDynamicPageLoader dynamicPageLoader,
        ILogger logger)
    {
        Sinks = sinks;
        LinkParser = linkParser;
        ContentParser = contentParser;
        StaticStaticPageLoader = staticPageLoader;
        DynamicPageLoader = dynamicPageLoader;

        Logger = logger;
    }

    public async Task<IEnumerable<Job>> CrawlAsync(Job job)
    {
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

            ScrapedData?.Invoke(result);

            var sinkTasks = Sinks.Select(sink => sink.EmitAsync(result));

            await Task.WhenAll(sinkTasks);
            return Enumerable.Empty<Job>();
        }

        var newLinkPathSelectors = job.LinkPathSelectors.Dequeue(out var currentSelector);

        var rawLinks = LinkParser.GetLinks(new Uri(job.Url), doc, currentSelector.Selector).ToList();

        var newJobs = new List<Job>();

        newJobs.AddRange(CreateNextJobs(job, currentSelector, newLinkPathSelectors, links));

        if (job.PageCategory == PageCategory.PageWithPagination)
        {
            ArgumentNullException.ThrowIfNull(currentSelector.PaginationSelector);

            var rawPaginatedLinks = LinkParser.GetLinks(new Uri(job.Url), doc, currentSelector.PaginationSelector);

            if (!rawPaginatedLinks.Any())
            {
                Logger.LogInformation("No pages with pagination found with selector {selector} on {url}", currentSelector.PaginationSelector, job.Url);
            }

            newJobs.AddRange(CreateNextJobs(job, currentSelector, job.LinkPathSelectors, rawPaginatedLinks));
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