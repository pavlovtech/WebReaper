using System.Net.Security;
using Fizzler.Systems.HtmlAgilityPack;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using WebReaper.Domain;
using WebReaper.Extensions;
using System.Diagnostics;
using System.Collections.Concurrent;
using WebReaper.Queue.Abstract;

namespace WebReaper.Spider.Concrete;

public class Spider
{
    protected IJobQueue jobsQueue { get; }

    protected ConcurrentDictionary<string, byte> visitedUrls = new ConcurrentDictionary<string, byte>();

    private ILogger _logger;

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

    public Spider(IJobQueue jobs, ILogger logger)
    {
        jobsQueue = jobs;
        _logger = logger;
    }

    public async Task Crawl()
    {
        Stopwatch watch = new Stopwatch();
        watch.Start();

        foreach (var job in jobsQueue.Get())
        {
            try
            {
                await Handle(job);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred when scraping {url}", job.Url);

                // return job back to the queue
                jobsQueue.Add(job);
            }
        }
    }

    protected async Task Handle(Job job)
    {
        visitedUrls.TryAdd(job.Url, 0);

        if(job.Url == "https://rutracker.org/forum/viewforum.php?f=396" ||
        job.Url == "https://rutracker.org/forum/viewforum.php?f=2322" ||
        job.Url == "https://rutracker.org/forum/viewforum.php?f=1993" ||
        job.Url == "https://rutracker.org/forum/viewforum.php?f=2167" ||
        job.Url == "https://rutracker.org/forum/viewforum.php?f=2321") {
            return;
        }

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

        if(job.DepthLevel < job.LinkPathSelectors.Length) {
            selectorIndex = job.DepthLevel;

        } else {
            selectorIndex = job.LinkPathSelectors.Length-1;
        }

        var selector = job.LinkPathSelectors[selectorIndex];

        var links = GetLinksFromPage(doc, job.BaseUrl, selector);

        PageType nextPageType = PageType.Unknown;
        if(selectorIndex == job.LinkPathSelectors.Length-1) {
            nextPageType = PageType.TargetPage;
        } else if(selectorIndex < job.LinkPathSelectors.Length) {
            nextPageType = PageType.TransitPage;
        }

        int nextPagePriority = -selectorIndex - 1; 

        AddToQueue(
                nextPageType,
                job.BaseUrl,
                job.PaginationSelector,
                nextPagePriority,
                job.DepthLevel+1,
                job.LinkPathSelectors,
                links);

        if(nextPageType == PageType.TargetPage && job.PaginationSelector != null) {
            var linksToPaginatedPages = GetLinksFromPage(doc, job.BaseUrl, job.PaginationSelector);

            AddToQueue(
                PageType.PageWithPagination,
                job.BaseUrl,
                job.PaginationSelector,
                job.Priority,
                job.DepthLevel+1,
                job.LinkPathSelectors,
                linksToPaginatedPages);
        }

        if(job.type == PageType.PageWithPagination) {
            var linksToPaginatedPages = GetLinksFromPage(doc, job.BaseUrl, job.PaginationSelector);

            AddToQueue(
                PageType.PageWithPagination,
                job.BaseUrl,
                job.PaginationSelector,
                job.Priority,
                job.DepthLevel+1,
                job.LinkPathSelectors,
                linksToPaginatedPages);
        }
    }

    private void AddToQueue(
        PageType type,
        string baseUrl,
        string paginationSelector,
        int priority,
        int depthLevel,
        string[] linkPathSelectors,
        IEnumerable<string> links)
    {
        var newLinks = links.Except(visitedUrls.Keys);

        foreach (var link in newLinks)
        {
            jobsQueue.Add(new Job(
                    baseUrl,
                    link,
                    linkPathSelectors,
                    //job.LinkPathSelector.Next, //fix
                    paginationSelector,
                    type,
                    depthLevel,
                    priority)); // fix
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