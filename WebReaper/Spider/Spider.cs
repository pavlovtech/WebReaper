using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using WebReaper.Domain;
using WebReaper.Extensions;
using System.Net;
using System.Text;
using WebReaper.LinkTracker.Abstract;
using WebReaper.Abastracts.Spider;
using WebReaper.Abstractions.Parsers;
using WebReaper.Absctracts.Sinks;
using WebReaper.Domain.Selectors;
using WebReaper.Abstractions.JobQueue;
using WebReaper.Domain.Schema;

namespace WebReaper.Spider;

public class Spider : ISpider
{
    protected ILinkParser LinkParser { get; init; }
    protected IContentParser ContentParser { get; init; }
    protected ILinkTracker LinkTracker { get; init; }
    protected IJobQueueReader JobQueueReader { get; init; }
    protected IJobQueueWriter JobQueueWriter { get; init; }
    protected HttpClient HttpClient { get; init; }
    protected ILogger Logger { get; init; }

    protected string[] UrlBlackList { get; set; } = Array.Empty<string>();

    protected int PageCrawlLimit { get; set; } = int.MaxValue;

    public List<IScraperSink> Sinks { get; set; }

    public Spider(
        List<IScraperSink> sinks,
        ILinkParser linkParser,
        IContentParser contentParser,
        ILinkTracker linkTracker,
        IJobQueueReader jobQueueReader,
        IJobQueueWriter jobQueueWriter,
        HttpClient httpClient,
        ILogger logger)
    {
        ServicePointManager.DefaultConnectionLimit = int.MaxValue;
        ServicePointManager.ServerCertificateValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        
        Sinks = sinks;
        LinkParser = linkParser;
        ContentParser = contentParser;
        LinkTracker = linkTracker;
        JobQueueReader = jobQueueReader;
        JobQueueWriter = jobQueueWriter;
        HttpClient = httpClient;

        Logger = logger;
    }

    public ISpider IgnoreUrls(params string[] urlBlackList)
    {
        this.UrlBlackList = urlBlackList;
        return this;
    }

    public ISpider Limit(int limit)
    {
        this.PageCrawlLimit = limit;
        return this;
    }


    public async Task Crawl()
    {
        foreach (var job in JobQueueReader.Read())
        {
            try
            {
                await Handle(job);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error occurred when scraping {url}", job.Url);

                // return job back to the queue
                JobQueueWriter.Write(job);
            }
        }
    }

    protected async Task Handle(Job job)
    {
        if (UrlBlackList.Contains(job.Url)) return;

        if (LinkTracker.GetVisitedLinks(job.BaseUrl).Count() >= PageCrawlLimit)
        {
            JobQueueWriter.CompleteAdding();
            return;
        }

        LinkTracker.AddVisitedLink(job.BaseUrl, job.Url);

        var doc = await HttpClient.GetStringAsync(job.Url);

        if (job.PageCategory == PageCategory.TargetPage)
        {
            Logger.LogInvocationCount("Handle on target page");
            var result = ContentParser.Parse(doc, job.schema);

            var sinkTasks = Sinks.Select(sink => sink.Emit(result));

            await Task.WhenAll(sinkTasks);
            return;
        }

        var newLinkPathSelectors = job.LinkPathSelectors.Dequeue(out var currentSelector);

        var links = LinkParser.GetLinks(doc, currentSelector.Selector)
            .Select(link => job.BaseUrl + link)
            .Except(LinkTracker.GetVisitedLinks(job.BaseUrl));

        AddToQueue(job.schema, job.BaseUrl, newLinkPathSelectors, links, job.DepthLevel + 1);

        if (job.PageCategory == PageCategory.PageWithPagination)
        {
            ArgumentNullException.ThrowIfNull(currentSelector.PaginationSelector);

            var linksToPaginatedPages = LinkParser.GetLinks(doc, currentSelector.PaginationSelector)
                .Select(link => job.BaseUrl + link)
                .Except(LinkTracker.GetVisitedLinks(job.BaseUrl));

            if (!linksToPaginatedPages.Any())
            {
                Logger.LogInformation("No pages with pagination found with selector {selector} on {url}", currentSelector.PaginationSelector, job.Url);
            }

            AddToQueue(job.schema, job.BaseUrl, job.LinkPathSelectors, linksToPaginatedPages, job.DepthLevel + 1);
        }
    }

    private void AddToQueue(
        SchemaElement[] schema,
        string baseUrl,
        ImmutableQueue<LinkPathSelector> selectors,
        IEnumerable<string> links,
        int depthLevel)
    {
        foreach (var link in links)
        {
            var newJob = new Job(schema, baseUrl, link, selectors, depthLevel);
            JobQueueWriter.Write(newJob);
        }
    }
}