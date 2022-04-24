using System.Collections.Immutable;
using System.Net.Security;
using Microsoft.Extensions.Logging;
using WebReaper.Domain;
using WebReaper.Extensions;
using System.Diagnostics;
using System.Collections.Concurrent;
using WebReaper.Queue.Abstract;
using WebReaper.Spider.Abastract;
using System.Net;
using System.Text;
using WebReaper.LinkTracker.Abstract;

namespace WebReaper.Spider.Concrete;

public class Spider : ISpider
{    
    private readonly ILinkParser linkParser;
    private readonly IContentParser contentParser;
    private readonly ILinkTracker linkTracker;
    private readonly IJobQueueReader jobQueueReader;

    private readonly IJobQueueWriter jobQueueWriter;
    private readonly HttpClient httpClient;
    private ILogger _logger;

    private string[] urlBlackList = Array.Empty<string>();

    public Spider(
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

        this.linkParser = linkParser;
        this.contentParser = contentParser;
        this.linkTracker = linkTracker;
        this.jobQueueReader = jobQueueReader;
        this.jobQueueWriter = jobQueueWriter;
        this.httpClient = httpClient;

        _logger = logger;
    }

    public ISpider IgnoreUrls(params string[] urlBlackList)
    {
        this.urlBlackList = urlBlackList;
        return this;
    }

    public async Task Crawl()
    {
        Stopwatch watch = new Stopwatch();
        watch.Start();

        foreach (var job in jobQueueReader.Read())
        {
            try
            {
                await Handle(job);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred when scraping {url}", job.Url);

                // return job back to the queue
                jobQueueWriter.Write(job);
            }
        }
    }

    protected async Task Handle(Job job)
    {
        if (urlBlackList.Contains(job.Url)) return;

        linkTracker.AddVisitedLink(job.BaseUrl, job.Url);

        var doc = await httpClient.GetStringAsync(job.Url);

        if (job.PageType == PageType.TargetPage)
        {
            _logger.LogInvocationCount("Handle on target page");
            // TODO: save to file or something
            var contetnt = contentParser.Parse(doc, job.schema);

            _logger.LogInformation("Parsed page: {page}", contetnt.ToString());

            //_logger.LogInformation("target page: {page}", doc.DocumentNode.QuerySelector("title").InnerText);
            return;
        }
        
        var newLinkPathSelectors = job.LinkPathSelectors.Dequeue(out var currentSelector);

        var links = linkParser.GetLinks(doc, currentSelector.Selector)
            .Select(link => job.BaseUrl + link)
            .Except(linkTracker.GetVisitedLinks(job.BaseUrl));

        AddToQueue(job.schema, job.BaseUrl, newLinkPathSelectors, links, job.DepthLevel + 1);

        if (job.PageType == PageType.PageWithPagination) 
        {
            var linksToPaginatedPages = linkParser.GetLinks(doc, currentSelector.PaginationSelector)
                .Select(link => job.BaseUrl + link)
                .Except(linkTracker.GetVisitedLinks(job.BaseUrl));

            if(!linksToPaginatedPages.Any())
            {
                _logger.LogInformation("No pages with pagination found with selector {selector} on {url}", currentSelector.PaginationSelector, job.Url);
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
            jobQueueWriter.Write(newJob);
        }
    }
}