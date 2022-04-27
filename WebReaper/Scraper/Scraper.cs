using System.Net;
using WebReaper.Domain;
using Microsoft.Extensions.Logging;
using WebReaper.Abstractions.Scraper;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Net.Security;
using WebReaper.Domain.Selectors;
using WebReaper.Absctracts.Sinks;
using WebReaper.Abastracts.Spider;
using WebReaper.Abstractions.Parsers;
using WebReaper.LinkTracker.Abstract;
using WebReaper.Abstractions.JobQueue;
using WebReaper.Parser;
using WebReaper.Queue;
using WebReaper.Sinks;
using WebReaper.Domain.Parsing;

namespace WebReaper.Scraper;

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

    private Schema? schema;

    private WebProxy? proxy;

    private WebProxy[] proxies = Array.Empty<WebProxy>();

    protected string baseUrl = "";

    protected readonly IJobQueueReader JobQueueReader;

    protected readonly IJobQueueWriter JobQueueWriter;

    protected ILogger Logger;

    protected ILinkParser LinkParser = new LinkParserByCssSelector();

    protected ILinkTracker SiteLinkTracker = new WebReaper.LinkTracker.Concrete.InMemoryLinkTracker();

    protected readonly IContentParser ContentParser;

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

    protected int ParallelismDegree { get; private set; } = 1;

    public Scraper(ILogger logger)
    {
        Logger = logger;

        ContentParser = new ContentParser(logger);

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
        SelectorType selectorType = SelectorType.Css,
        PageType pageType = PageType.Static)
    {
        linkPathSelectors.Add(new(linkSelector, SelectorType: selectorType, PageType: pageType));
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

    public IScraper WithScheme(Schema schema)
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
            schema!,
            baseUrl,
            startUrl!,
            ImmutableQueue.Create<LinkPathSelector>(linkPathSelectors.ToArray()),
            DepthLevel: 0));

        var spiderTasks = Enumerable
            .Range(0, ParallelismDegree)
            .Select(_ => spider.Crawl());

        await Task.WhenAll(spiderTasks);
    }

    public IScraper WriteToConsole() =>
        this.AddSink(new ConsoleSink());

    public IScraper WriteToJsonFile(string filePath) =>
        this.AddSink(new JsonFileSink(filePath));

    public IScraper WriteToCsvFile(string filePath) =>
        this.AddSink(new CsvFileSink(filePath));

    public IScraper Build()
    {
        ArgumentNullException.ThrowIfNull(startUrl);
        ArgumentNullException.ThrowIfNull(schema);

        spider = new WebReaper.Spider.Spider(
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
