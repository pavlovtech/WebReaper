using System.Net;
using Microsoft.Extensions.Logging;
using System.Net.Security;
using WebReaper.Absctracts.Sinks;
using WebReaper.Abastracts.Spider;
using WebReaper.Abstractions.Parsers;
using WebReaper.LinkTracker.Abstract;
using WebReaper.Abstractions.JobQueue;
using WebReaper.Parser;
using WebReaper.Sinks;
using WebReaper.LinkTracker;
using WebReaper.Loaders;
using Microsoft.Extensions.Logging.Abstractions;
using WebReaper.Spiders;

namespace WebReaper.Scraper;

public class SpiderBuilder
{
    public List<IScraperSink> Sinks { get; protected set; } = new(); 
    
    protected int limit = int.MaxValue;

    protected string baseUrl = "";

    protected IJobQueueReader JobQueueReader;

    protected IJobQueueWriter JobQueueWriter;

    protected ILogger Logger = NullLogger.Instance;

    protected ILinkParser LinkParser = new LinkParserByCssSelector();

    protected ICrawledLinkTracker SiteLinkTracker = new InMemoryCrawledLinkTracker();

    protected IContentParser ContentParser;

    protected static SocketsHttpHandler httpHandler = new()
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

    protected Lazy<HttpClient> httpClient = new(() => new(httpHandler));

    protected List<string> urlBlackList = new();

    public SpiderBuilder WithLogger(ILogger logger)
    {
        Logger = logger;
        return this;
    }

    public SpiderBuilder WithLinkTracker(ICrawledLinkTracker linkTracker)
    {
        SiteLinkTracker = linkTracker;
        return this;
    }

    public SpiderBuilder WithJobQueueWriter(IJobQueueWriter jobQueueWriter)
    {
        JobQueueWriter = jobQueueWriter;
        return this;
    }

    public SpiderBuilder WithJobQueueReader(IJobQueueReader jobQueueReader)
    {
        JobQueueReader = jobQueueReader;
        return this;
    }

    public SpiderBuilder Authorize(Func<CookieContainer> authorize)
    {
        var CookieContainer = authorize();

        httpHandler.CookieContainer = CookieContainer;

        return this;
    }

    public SpiderBuilder AddSink(IScraperSink sink)
    {
        Sinks.Add(sink);

        return this;
    }

    public SpiderBuilder IgnoreUrls(params string[] urls)
    {
        urlBlackList.AddRange(urls);
        return this;
    }

    public SpiderBuilder Limit(int limit)
    {
        this.limit = limit;
        return this;
    }

    public SpiderBuilder WriteToConsole() => AddSink(new ConsoleSink());

    public SpiderBuilder WriteToJsonFile(string filePath) => AddSink(new JsonFileSink(filePath));

    public SpiderBuilder WriteToCsvFile(string filePath) => AddSink(new CsvFileSink(filePath));

    public SpiderBuilder Build()
    {
        ISpider spider = new WebReaperSpider(
            Sinks,
            LinkParser,
            new ContentParser(Logger),
            SiteLinkTracker,
            new HttpPageLoader(httpClient.Value, Logger),
            new PuppeteerPageLoader(Logger),
            JobQueueReader,
            JobQueueWriter,
            Logger)
        {
            UrlBlackList = urlBlackList.ToList(),

            PageCrawlLimit = limit
        };

        return this;
    }
}
