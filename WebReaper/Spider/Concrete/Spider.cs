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

namespace WebReaper.Spider.Concrete;

public class Spider : ISpider
{
    protected ConcurrentDictionary<string, byte> visitedUrls = new();
    
    private readonly ILinkParser linkParser;

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
        ILinkParser linkParser,
        IJobQueueReader jobQueueReader,
        IJobQueueWriter jobQueueWriter,
        ILogger logger)
    {
        ServicePointManager.DefaultConnectionLimit = int.MaxValue;
        ServicePointManager.ServerCertificateValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        this.linkParser = linkParser;
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

        var doc = await httpClient.GetStringAsync(job.Url);

        if (job.PageType == PageType.TargetPage)
        {
            _logger.LogInvocationCount("Handle on target page");
            // TODO: save to file or something
            //_logger.LogInformation("target page: {page}", doc.DocumentNode.QuerySelector("title").InnerText);
            return;
        }
        
        var newLinkPathSelectors = job.LinkPathSelectors.Dequeue(out var currentSelector);

        var links = linkParser.GetLinks(doc, currentSelector.Selector)
            .Select(link => job.BaseUrl + link);

        AddToQueue(job.BaseUrl, newLinkPathSelectors, links, job.DepthLevel + 1);

        if (job.PageType == PageType.PageWithPagination) 
        {
            var linksToPaginatedPages = linkParser.GetLinks(doc, currentSelector.PaginationSelector)
                .Select(link => job.BaseUrl + link);

            AddToQueue(job.BaseUrl, job.LinkPathSelectors, linksToPaginatedPages, job.DepthLevel + 1);
        }
    }

    private void AddToQueue(
        string baseUrl,
        ImmutableQueue<LinkPathSelector> selectors,
        IEnumerable<string> links,
        int depthLevel)
    {
        var newLinks = links.Except(visitedUrls.Keys);

        foreach (var link in newLinks)
        {
            var newJob = new Job(baseUrl, link, selectors, depthLevel);
            jobQueueWriter.Write(newJob);
        }
    }
}