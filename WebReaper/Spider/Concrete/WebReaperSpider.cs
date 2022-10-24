﻿using System.Collections.Immutable;
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
using WebReaper.Loaders.Concrete;

namespace WebReaper.Spider.Concrete;

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

    public async Task<IEnumerable<Job>> CrawlAsync(Job job, CancellationToken cancellationToken = default)
    {
        if (UrlBlackList.Contains(job.Url)) return Enumerable.Empty<Job>();

        if (await LinkTracker.GetVisitedLinksCount(job.SiteId) >= PageCrawlLimit)
        {
            Logger.LogInformation("Page crawl limit has been reached.");

            throw new PageCrawlLimitException("Page crawl limit has been reached.") 
            {
                PageCrawlLimit = this.PageCrawlLimit
            };
        }

        await LinkTracker.AddVisitedLinkAsync(job.SiteId, job.Url);

        string doc;

        if (job.PageType == PageType.Static)
        {
            Logger.LogInformation("Loading static page {URL}", job.Url);
            doc = await StaticStaticPageLoader.Load(job.Url);
        }
        else
        {
            Logger.LogInformation("Loading dynamic page {URL}", job.Url);
            doc = await DynamicPageLoader.Load(job.Url, job.Script);
        }

        //Logger.LogDebug(doc);

        if (job.PageCategory == PageCategory.TargetPage)
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
            return Enumerable.Empty<Job>();
        }

        var newLinkPathSelectors = job.LinkPathSelectors.Dequeue(out var currentSelector);

        var baseUrl = new Uri(job.Url);

        Logger.LogDebug("Base url: {baseUrl}", baseUrl);

        var rawLinks = LinkParser.GetLinks(baseUrl, doc, currentSelector.Selector).ToList();

        var links = rawLinks
            .Except(await LinkTracker.GetVisitedLinksAsync(job.SiteId));

        var newJobs = new List<Job>();

        newJobs.AddRange(CreateNextJobs(job, currentSelector, newLinkPathSelectors, links, cancellationToken));

        if (job.PageCategory == PageCategory.PageWithPagination)
        {
            ArgumentNullException.ThrowIfNull(currentSelector.PaginationSelector);

            var rawPaginatedLinks = LinkParser.GetLinks(baseUrl, doc, currentSelector.PaginationSelector);

            Logger.LogInformation("Found {pages} with pagination", rawPaginatedLinks.Count());

            if (!rawPaginatedLinks.Any())
            {
                Logger.LogInformation("No pages with pagination found with selector {selector} on {url}", currentSelector.PaginationSelector, job.Url);
            }

            var linksToPaginatedPages = await LinkTracker.GetNotVisitedLinks(job.SiteId, rawPaginatedLinks);

            newJobs.AddRange(CreateNextJobs(job, currentSelector, job.LinkPathSelectors, linksToPaginatedPages));
        }

        return newJobs;
    }

    private IEnumerable<Job> CreateNextJobs(
        Job job,
        LinkPathSelector currentSelector,
        ImmutableQueue<LinkPathSelector> selectors,
        IEnumerable<string> links,
        CancellationToken cancellationToken = default)
    {
        foreach (var link in links)
        {
            if(cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var newJob = job with { Url = link, LinkPathSelectors = selectors, PageType = currentSelector.PageType, Script = currentSelector.ScriptExpression };
            yield return newJob;
        }
    }
}