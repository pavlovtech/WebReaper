using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using WebReaper.Sinks.Concrete;
using WebReaper.Domain.Selectors;
using WebReaper.Domain.Parsing;
using WebReaper.LinkTracker.Abstract;
using WebReaper.Sinks.Abstract;
using WebReaper.Scheduler.Abstract;
using WebReaper.Scheduler.Concrete;
using WebReaper.Proxy.Abstract;
using WebReaper.Logging;
using System.Collections.Immutable;
using WebReaper.LinkTracker.Concrete;
using WebReaper.PageActions;

namespace WebReaper.Core.Builders;

public class WebReaperBuilder
{
    private ConfigBuilder ConfigBuilder { get; } = new();
    private SpiderBuilder SpiderBuilder { get; } = new();

    private ILogger Logger { get; set; } = NullLogger.Instance;

    private IScheduler Scheduler { get; set; } = new InMemoryScheduler();

    private string SiteId { get; }

    protected IProxyProvider? ProxyProvider { get; set; }

    public WebReaperBuilder(string siteId)
    {
        SiteId = siteId;
    }

    public WebReaperBuilder AddSink(IScraperSink sink)
    {
        SpiderBuilder.AddSink(sink);
        return this;
    }

    public WebReaperBuilder Authorize(Func<CookieContainer> authorize)
    {
        SpiderBuilder.Authorize(authorize);
        return this;
    }

    public WebReaperBuilder IgnoreUrls(params string[] urls)
    {
        SpiderBuilder.IgnoreUrls(urls);
        return this;
    }

    public WebReaperBuilder Limit(int limit)
    {
        SpiderBuilder.Limit(limit);
        return this;
    }

    public WebReaperBuilder WithLinkTracker(IVisitedLinkTracker linkTracker)
    {
        SpiderBuilder.WithLinkTracker(linkTracker);
        return this;
    }
    
    public WebReaperBuilder TrackVisitedLinksInFile(string fileName)
    {
        SpiderBuilder.WithLinkTracker(new FileVisitedLinkedTracker(fileName));
        return this;
    }
    
    public WebReaperBuilder TrackVisitedLinksInRedis(string connectionString)
    {
        SpiderBuilder.WithLinkTracker(new RedisVisitedLinkTracker(connectionString));
        return this;
    }

    public WebReaperBuilder WithLogger(ILogger logger)
    {
        SpiderBuilder.WithLogger(logger);

        Logger = logger;

        return this;
    }

    public WebReaperBuilder LogToConsole()
    {
        SpiderBuilder.WithLogger(new ColorConsoleLogger());

        return this;
    }

    public WebReaperBuilder WriteToConsole()
    {
        SpiderBuilder.WriteToConsole();
        return this;
    }

    public WebReaperBuilder Subscribe(Action<JObject> scrapingResultHandler)
    {
        SpiderBuilder.AddSubscription(scrapingResultHandler);
        return this;
    }

    public WebReaperBuilder WriteToCosmosDb(
        string endpointUrl,
        string authorizationKey,
        string databaseId,
        string containerId)
    {
        SpiderBuilder.AddSink(new CosmosSink(endpointUrl, authorizationKey, databaseId, containerId, Logger));
        return this;
    }

    public WebReaperBuilder WriteToMongoDb(string connectionString, string databaseName, string collectionName)
    {
        SpiderBuilder.AddSink(new MongoDbSink(connectionString, databaseName, collectionName, Logger));
        return this;
    }

    public WebReaperBuilder WriteToCsvFile(string filePath)
    {
        SpiderBuilder.AddSink(new CsvFileSink(filePath));
        return this;
    }

    public WebReaperBuilder WriteToJsonFile(string filePath)
    {
        SpiderBuilder.AddSink(new JsonLinesFileSink(filePath));
        return this;
    }

    public WebReaperBuilder WithProxies(IProxyProvider proxyProvider)
    {
        SpiderBuilder.WithProxies(proxyProvider);
        return this;
    }

    public WebReaperBuilder Parse(Schema schema)
    {
        ConfigBuilder.WithScheme(schema);
        return this;
    }

    public WebReaperBuilder Get(string url)
    {
        ConfigBuilder.Get(url);
        return this;
    }

    public WebReaperBuilder GetWithBrowser(
        string url,
        Func<PageActionBuilder, ImmutableQueue<PageAction>>? actionBuilder = null)
    {
        ConfigBuilder.GetWithBrowser(url, actionBuilder?.Invoke(new()));
        return this;
    }

    public WebReaperBuilder Follow(string linkSelector)
    {
        ConfigBuilder.Follow(linkSelector);
        return this;
    }

    public WebReaperBuilder FollowWithBrowser(
        string linkSelector,
        Func<PageActionBuilder, ImmutableQueue<PageAction>>? actionBuilder = null)
    {
        ConfigBuilder.FollowWithBrowser(linkSelector, actionBuilder?.Invoke(new()));
        return this;
    }

    public WebReaperBuilder Paginate(
        string linkSelector,
        string paginationSelector)
    {
        ConfigBuilder.Paginate(linkSelector, paginationSelector);
        return this;
    }

    public WebReaperBuilder PaginateWithBrowser(
        string linkSelector,
        string paginationSelector,
        Func<PageActionBuilder, ImmutableQueue<PageAction>>? actionBuilder = null)
    {
        ConfigBuilder.PaginateWithBrowser(linkSelector, paginationSelector, actionBuilder?.Invoke(new()));
        return this;
    }

    public WebReaperBuilder WithScheduler(IScheduler scheduler)
    {
        Scheduler = scheduler;
        return this;
    }
    
    public WebReaperBuilder WithAzureServiceBusScheduler(string connectionString, string queueName)
    {
        Scheduler = new AzureServiceBusScheduler(connectionString, queueName);
        return this;
    }
    
    public WebReaperBuilder WithTextFileScheduler(string fileName, string currentJobPositionFileName)
    {
        Scheduler = new FileScheduler(fileName, currentJobPositionFileName, Logger);
        return this;
    }
    
    public WebReaperBuilder WithRedisScheduler(string connectionString, string queueName)
    {
        Scheduler = new RedisScheduler(connectionString, queueName, Logger);
        return this;
    }

    public ScraperEngine Build()
    {
        var config = ConfigBuilder.Build();
        var spider = SpiderBuilder.Build();

        return new ScraperEngine(SiteId, config, Scheduler, spider, Logger);
    }
}
