using System.Net.Security;
using Fizzler.Systems.HtmlAgilityPack;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using WebReaper.Domain;
using WebReaper.Infra;
using WebReaper.Queue;

public class Spider
{
    protected IJobQueue jobsQueue { get; }

    private ILogger<Spider> _logger;

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

    public Spider(IJobQueue jobs, ILogger<Spider> logger)
    {
        jobsQueue = jobs;
        _logger = logger;
    }

    public async Task Crawl()
    {
        foreach (var job in jobsQueue.GetJobs())
        {
            var doc = await GetDocumentAsync(job.Url);

            if(job.GetPageType() == PageType.TargetPage) {
                // TODO: save to file or something
                _logger.LogInformation("target page: {page}", doc.DocumentNode.InnerHtml);
                continue;
            }

            IEnumerable<string> links = Enumerable.Empty<string>();

            if(job.GetPageType() == PageType.PageWithPagination) {
                links = GetLinksFromPage(doc, job.BaseUrl, job.PaginationSelector);
            }
            else if(job.GetPageType() == PageType.TransitPage)
            {
                var selector = job.LinkPathSelector?.Value;
                links = GetLinksFromPage(doc, job.BaseUrl, selector);
            }

            foreach (var link in links)
            {            
                jobsQueue.Add(new Job(
                        job.BaseUrl,
                        link,
                        job.LinkPathSelector.Next,
                        job.PaginationSelector,
                        job.Priority + 1));
            }
        }
    }

    protected static async Task<HtmlDocument> GetDocumentAsync(string url)
    {
        static async Task<HtmlDocument> GetDocumentInternalAsync(string url)
        {
            var htmlDoc = new HtmlDocument();
            var html = await httpClient.GetStringAsync(url);
            htmlDoc.LoadHtml(html);
            return htmlDoc;
        }

        return await Executor.Run(async () => await GetDocumentInternalAsync(url));
    }

    private IEnumerable<string> GetLinksFromPage(HtmlDocument document, string baseUrl, string selector)
    {
        return document
            .DocumentNode
            .QuerySelectorAll(selector)
            .Select(e => baseUrl + HtmlEntity.DeEntitize(e.GetAttributeValue("href", null)))
            .Distinct();
    }
}