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

namespace WebReaper.Core.Builders;

public class EngineBuilder
{
    private ConfigBuilder ConfigBuilder { get; } = new();
    private SpiderBuilder SpiderBuilder { get; } = new();

    private ILogger Logger { get; set; } = NullLogger.Instance;

    private IScheduler Scheduler { get; set; } = new InMemoryScheduler();

    private string SiteId { get; }

    protected IProxyProvider? ProxyProvider { get; set; }

    public EngineBuilder(string siteId)
    {
        SiteId = siteId;
    }

    public EngineBuilder AddSink(IScraperSink sink)
    {
        SpiderBuilder.AddSink(sink);
        return this;
    }

    public EngineBuilder Authorize(Func<CookieContainer> authorize)
    {
        SpiderBuilder.Authorize(authorize);
        return this;
    }

    public EngineBuilder IgnoreUrls(params string[] urls)
    {
        SpiderBuilder.IgnoreUrls(urls);
        return this;
    }

    public EngineBuilder Limit(int limit)
    {
        SpiderBuilder.Limit(limit);
        return this;
    }

    public EngineBuilder WithLinkTracker(IVisitedLinkTracker linkTracker)
    {
        SpiderBuilder.WithLinkTracker(linkTracker);
        return this;
    }

    public EngineBuilder WithLogger(ILogger logger)
    {
        SpiderBuilder.WithLogger(logger);

        Logger = logger;

        return this;
    }

    public EngineBuilder LogToConsole()
    {
        SpiderBuilder.WithLogger(new ColorConsoleLogger());

        return this;
    }

    public EngineBuilder WriteToConsole()
    {
        SpiderBuilder.WriteToConsole();
        return this;
    }

    public EngineBuilder Subscribe(Action<JObject> scrapingResultHandler)
    {
        SpiderBuilder.AddSubscription(scrapingResultHandler);
        return this;
    }

    public EngineBuilder WriteToCosmosDb(
        string endpointUrl,
        string authorizationKey,
        string databaseId,
        string containerId)
    {
        SpiderBuilder.AddSink(new CosmosSink(endpointUrl, authorizationKey, databaseId, containerId, Logger));
        return this;
    }

    public EngineBuilder WriteToMongoDb(string connectionString, string databaseName, string collectionName)
    {
        SpiderBuilder.AddSink(new MongoDbSink(connectionString, databaseName, collectionName, Logger));
        return this;
    }

    public EngineBuilder WriteToCsvFile(string filePath)
    {
        SpiderBuilder.AddSink(new CsvFileSink(filePath));
        return this;
    }

    public EngineBuilder WriteToJsonFile(string filePath)
    {
        SpiderBuilder.AddSink(new JsonLinesFileSink(filePath));
        return this;
    }

    public EngineBuilder WithProxies(IProxyProvider proxyProvider)
    {
        SpiderBuilder.WithProxies(proxyProvider);
        return this;
    }

    public EngineBuilder Parse(Schema schema)
    {
        ConfigBuilder.WithScheme(schema);
        return this;
    }

    public EngineBuilder Get(string url, string? script = null)
    {
        ConfigBuilder.Get(url, script);
        return this;
    }

    public EngineBuilder GetWithBrowser(string url, PageType pageType = PageType.Static, string? script = null)
    {
        ConfigBuilder.GetWithBrowser(url, script);
        return this;
    }

    public EngineBuilder Follow(
        string linkSelector,
        string? script = null)
    {
        ConfigBuilder.Follow(linkSelector,  script);
        return this;
    }

    public EngineBuilder FollowWithBrowser(
    string linkSelector,
    string? script = null)
    {
        ConfigBuilder.FollowWithBrowser(linkSelector, script);
        return this;
    }

    public EngineBuilder Paginate(
        string linkSelector,
        string paginationSelector,
        string? script = null)
    {
        ConfigBuilder.Paginate(linkSelector, paginationSelector, script);
        return this;
    }

    public EngineBuilder PaginateWithBrowser(
        string linkSelector,
        string paginationSelector,
        string? script = null)
    {
        ConfigBuilder.PaginateWithBrowser(linkSelector, paginationSelector, script);
        return this;
    }

    public EngineBuilder WithScheduler(IScheduler scheduler)
    {
        Scheduler = scheduler;
        return this;
    }

    public ScraperEngine Build()
    {
        var config = ConfigBuilder.Build();
        var spider = SpiderBuilder.Build();

        return new ScraperEngine(SiteId, config, Scheduler, spider, Logger);
    }
}
