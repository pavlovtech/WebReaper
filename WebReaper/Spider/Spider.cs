using System.Net.Security;
using Fizzler.Systems.HtmlAgilityPack;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using WebReaper.Domain;
using WebReaper.Queue;
using WebReaper.Extensions;

namespace WebReaper.Spider;

public class Spider
{
    protected IJobQueue jobsQueue { get; }

    private ILogger _logger;

    protected static HttpClient httpClient = new HttpClient(new SocketsHttpHandler()
    {
        MaxConnectionsPerServer = 100,
        SslOptions = new SslClientAuthenticationOptions
        {
            // Leave certs unvalidated for debugging
            RemoteCertificateValidationCallback = delegate { return true; },
        },
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(10),
        PooledConnectionLifetime = Timeout.InfiniteTimeSpan
    })
    {
        Timeout = TimeSpan.FromMinutes(10)
    };

    public Spider(IJobQueue jobs, ILogger logger)
    {
        jobsQueue = jobs;
        _logger = logger;
    }

    public async Task Crawl()
    {
        foreach (var job in jobsQueue.GetJobs())
            await Handle(job);
    }

    protected async Task Handle(Job job)
    {
        using var _ = _logger.Measure();
        
        var doc = await GetDocumentAsync(job.Url);

        if (job.GetPageType() == PageType.TargetPage)
        {
            // TODO: save to file or something
            _logger.LogInformation("target page: {page}", doc.DocumentNode.QuerySelector("title").InnerText);
            return;
        }

        IEnumerable<string> links = Enumerable.Empty<string>();

        if (job.GetPageType() == PageType.PageWithPagination)
        {
            links = GetLinksFromPage(doc, job.BaseUrl, job.PaginationSelector);

            var selector = job.LinkPathSelector?.Value;
            links = links.Concat(GetLinksFromPage(doc, job.BaseUrl, selector));
        }
        else if (job.GetPageType() == PageType.TransitPage)
        {
            var selector = job.LinkPathSelector?.Value;
            links = GetLinksFromPage(doc, job.BaseUrl, selector);
        }

        foreach (var link in links)
        {
            jobsQueue.Add(new Job(
                    job.BaseUrl,
                    link,
                    job.LinkPathSelector.Next, // fix
                    job.PaginationSelector,
                    job.Priority + 1)); // fix
        }
    }

    protected async Task<HtmlDocument> GetDocumentAsync(string url)
    {
        using var _ = _logger.Measure();

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
        using var _ = _logger.Measure();

        return document.DocumentNode
            .QuerySelectorAll(selector)
            .Select(e => baseUrl + HtmlEntity.DeEntitize(e.GetAttributeValue("href", null)))
            .Distinct();
    }
}