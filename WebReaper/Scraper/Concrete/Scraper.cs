using System.Net;
using WebReaper.Domain;
using Microsoft.Extensions.Logging;
using WebReaper.Queue.Abstract;
using WebReaper.Scraper.Abstract;
using WebReaper.Queue.Concrete;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using WebReaper.Parser.Concrete;
using System.Net.Security;
using WebReaper.LinkTracker.Abstract;
using WebReaper.Sinks.Absctract;
using WebReaper.Spider.Abastract;

namespace WebReaper.Scraper.Concrete;

// anglesharpjs
// puppeter
public class Scraper : IScraper
{
    public List<IScraperSink> Sinks { get; protected set; } = new(); 

    protected List<LinkPathSelector> linkPathSelectors = new();
    
    protected int limit = int.MaxValue;

    protected BlockingCollection<Job> jobs = new(new ProducerConsumerPriorityQueue());

    private string? startUrl;
    
    private ISpider spider;

    private SchemaElement[]? schema = Array.Empty<SchemaElement>();

    private WebProxy? proxy;

    private WebProxy[] proxies = Array.Empty<WebProxy>();

    private int parallelismDegree = 1;

    protected string baseUrl = "";

    protected readonly IJobQueueReader JobQueueReader;

    protected readonly IJobQueueWriter JobQueueWriter;

    protected ILogger Logger;

    protected ILinkParser LinkParser = new LinkParserByCssSelector();

    protected ILinkTracker SiteLinkTracker = new WebReaper.LinkTracker.Concrete.LinkTracker();

    protected IContentParser ContentParser = new ContentParser();

    protected static SocketsHttpHandler httpHandler = new SocketsHttpHandler()
    {
        MaxConnectionsPerServer = 100,
        SslOptions = new SslClientAuthenticationOptions
        {
            // Leave certs unvalidated for debugging
            RemoteCertificateValidationCallback = delegate { return true; },
        },
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
        PooledConnectionLifetime = Timeout.InfiniteTimeSpan
    };

    protected Lazy<HttpClient> httpClient = new Lazy<HttpClient>(() => new HttpClient(httpHandler));

    protected string[] urlBlackList = Array.Empty<string>();

    protected int ParallelismDegree { get; private set; }

    public Scraper(ILogger logger)
    {
        Logger = logger;

        JobQueueReader = new JobQueueReader(jobs);
        JobQueueWriter = new JobQueueWriter(jobs);
    }

    public IScraper WithStartUrl(string startUrl)
    {
        this.startUrl = startUrl;

        var startUri = new Uri(startUrl);

        var baseUrl = startUri.GetLeftPart(UriPartial.Authority);
        var segments = startUri.Segments;

        this.baseUrl = baseUrl + string.Join(string.Empty, segments.SkipLast(1));

        return this;
    }

    public IScraper Authorize(Func<CookieContainer> authorize)
    {
        var CookieContainer = authorize();

        httpHandler.CookieContainer = CookieContainer;

        return this;
    }

    public IScraper FollowLinks(
        string linkSelector,
        SelectorType selectorType = SelectorType.Css)
    {
        linkPathSelectors.Add(new(linkSelector));
        return this;
    }

    public IScraper Paginate(string paginationSelector)
    {
        linkPathSelectors[^1] =
            linkPathSelectors.Last() with
            {
                PaginationSelector = paginationSelector,
            };

        return this;
    }

    public ScraperSinkConfig WriteTo => new ScraperSinkConfig(this);

    public IScraper AddSink(IScraperSink sink)
    {
        this.Sinks.Add(sink);

        return this;
    }

    public IScraper IgnoreUrls(params string[] urls)
    {
        this.urlBlackList = urls;
        return this;
    }

    public IScraper WithScheme(SchemaElement[] schema)
    {
        this.schema = schema;
        return this;
    }

    public IScraper WithParallelismDegree(int parallelismDegree)
    {
        this.ParallelismDegree = parallelismDegree;
        return this;
    }

    public IScraper Limit(int limit)
    {
        this.limit = limit;
        return this;
    }

    public IScraper WithProxy(WebProxy proxy)
    {
        this.proxy = proxy;
        return this;
    }

    public IScraper WithProxy(WebProxy[] proxies)
    {
        this.proxies = proxies;
        return this;
    }

    public IScraper WithPuppeter(WebProxy[] proxies)
    {
        return this;
    }

    public async Task Run()
    {
        JobQueueWriter.Write(new Job(
            schema,
            baseUrl,
            startUrl,
            ImmutableQueue.Create<LinkPathSelector>(linkPathSelectors.ToArray()),
            DepthLevel: 0));

        var spiderTasks = Enumerable
            .Range(0, parallelismDegree)
            .Select(_ => spider.Crawl());

        await Task.WhenAll(spiderTasks);
    }

    public IScraper Build()
    {
        ArgumentNullException.ThrowIfNull(startUrl);
        ArgumentNullException.ThrowIfNull(schema);

        spider = new WebReaper.Spider.Concrete.Spider(
            Sinks,
            LinkParser,
            ContentParser,
            SiteLinkTracker,
            JobQueueReader,
            JobQueueWriter,
            httpClient.Value,
            Logger)
            .IgnoreUrls(this.urlBlackList)
            .Limit(limit);

        return this;
    }
}
