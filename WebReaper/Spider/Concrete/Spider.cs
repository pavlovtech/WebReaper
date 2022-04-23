using System.Net.Security;
using Fizzler.Systems.HtmlAgilityPack;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using WebReaper.Domain;
using WebReaper.Extensions;
using System.Diagnostics;
using System.Collections.Concurrent;
using WebReaper.Queue.Abstract;
using WebReaper.Spider.Abastract;
using System.Net;
using System.Text;

namespace WebReaper.Spider.Concrete;

public class Spider : ISpider
{
    protected ConcurrentDictionary<string, byte> visitedUrls = new();

    private readonly IJobQueueReader jobQueueReader;

    private readonly IJobQueueWriter jobQueueWriter;

    private ILogger _logger;

    private string[] urlBlackList = Array.Empty<string>();

    protected static HttpClient httpClient = new(new SocketsHttpHandler()
    {
        MaxConnectionsPerServer = 100,
        SslOptions = new SslClientAuthenticationOptions
        {
            // Leave certs unvalidated for debugging
            RemoteCertificateValidationCallback = delegate { return true; },
        },
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
        PooledConnectionLifetime = Timeout.InfiniteTimeSpan
    })
    {
        //Timeout = TimeSpan.FromMinutes(2)
    };

    public Spider(
        IJobQueueReader jobQueueReader,
        IJobQueueWriter jobQueueWriter,
        ILogger logger)
    {
        ServicePointManager.DefaultConnectionLimit = int.MaxValue;
        ServicePointManager.ServerCertificateValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        this.jobQueueReader = jobQueueReader;
        this.jobQueueWriter = jobQueueWriter;
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

        visitedUrls.TryAdd(job.Url, 0);

        using var _ = _logger.LogMethodDuration();

        var doc = await GetDocumentAsync(job.Url);

        if (job.type == PageType.TargetPage)
        {
            _logger.LogInvocationCount("Handle on target page");
            // TODO: save to file or something
            _logger.LogInformation("target page: {page}", doc.DocumentNode.QuerySelector("title").InnerText);
            return;
        }

        int selectorIndex = 0;

        if (job.DepthLevel < job.LinkPathSelectors.Length)
        {
            selectorIndex = job.DepthLevel;
        }
        else
        {
            selectorIndex = job.LinkPathSelectors.Length - 1;
        }

        var linkPath = job.LinkPathSelectors[selectorIndex];

        var links = GetLinksFromPage(doc, job.BaseUrl, linkPath.Selector);

        PageType nextPageType = PageType.Unknown;
        if (selectorIndex == job.LinkPathSelectors.Length - 1)
        {
            nextPageType = PageType.TargetPage;
        }
        else if (selectorIndex < job.LinkPathSelectors.Length)
        {
            nextPageType = PageType.TransitPage;
        }

        int nextPagePriority = -selectorIndex - 1;

        AddToQueue(
                nextPageType,
                job.BaseUrl,
                nextPagePriority,
                job.DepthLevel + 1,
                job.LinkPathSelectors,
                links);

        if (nextPageType == PageType.TargetPage && job.LinkPathSelectors[selectorIndex].PaginationSelector != null)
        {
            var linksToPaginatedPages = GetLinksFromPage(doc, job.BaseUrl, job.LinkPathSelectors[selectorIndex].PaginationSelector);

            AddToQueue(
                PageType.PageWithPagination,
                job.BaseUrl,
                job.Priority,
                job.DepthLevel + 1,
                job.LinkPathSelectors,
                linksToPaginatedPages);
        }

        if (job.type == PageType.PageWithPagination)
        {
            var linksToPaginatedPages = GetLinksFromPage(
            doc,
            job.BaseUrl,
            job.LinkPathSelectors[selectorIndex].PaginationSelector);

            AddToQueue(
                PageType.PageWithPagination,
                job.BaseUrl,
                job.Priority,
                job.DepthLevel + 1,
                job.LinkPathSelectors,
                linksToPaginatedPages);
        }
    }

    private void AddToQueue(
        PageType type,
        string baseUrl,
        int priority,
        int depthLevel,
        LinkPathSelector[] linkPathSelectors,
        IEnumerable<string> links)
    {
        var newLinks = links.Except(visitedUrls.Keys);

        foreach (var link in newLinks)
        {
            jobQueueWriter.Write(new Job(
                    baseUrl,
                    link,
                    linkPathSelectors,
                    type,
                    depthLevel,
                    priority));
        }
    }

    protected async Task<HtmlDocument> GetDocumentAsync(string url)
    {
        using var _ = _logger.LogMethodDuration();

        var htmlDoc = new HtmlDocument();
        var html = await httpClient.GetStringAsync(url);
        htmlDoc.LoadHtml(html);
        return htmlDoc;
    }

    private IEnumerable<string> GetLinksFromPage(
        HtmlDocument document,
        string baseUrl,
        string selector)
    {
        return document.DocumentNode
            .QuerySelectorAll(selector)
            .Select(e => baseUrl + HtmlEntity.DeEntitize(e.GetAttributeValue("href", null)))
            .Distinct();
    }
}