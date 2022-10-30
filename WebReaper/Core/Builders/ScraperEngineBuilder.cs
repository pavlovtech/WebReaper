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

namespace WebReaper.Core.Builders;

public class ScraperEngineBuilder
{
    private ScraperConfigBuilder ConfigBuilder { get; } = new();
    private SpiderBuilder SpiderBuilder { get; } = new();

    private ILogger Logger { get; set; } = NullLogger.Instance;

    private IScheduler Scheduler { get; set; } = new InMemoryScheduler();

    private string SiteId { get; }

    protected IProxyProvider ProxyProvider { get; set; }

    public ScraperEngineBuilder(string siteId)
    {
        SiteId = siteId;
    }

    public ScraperEngineBuilder AddSink(IScraperSink sink)
    {
        SpiderBuilder.AddSink(sink);
        return this;
    }

    public ScraperEngineBuilder Authorize(Func<CookieContainer> authorize)
    {
        SpiderBuilder.Authorize(authorize);
        return this;
    }

    public ScraperEngineBuilder IgnoreUrls(params string[] urls)
    {
        SpiderBuilder.IgnoreUrls(urls);
        return this;
    }

    public ScraperEngineBuilder Limit(int limit)
    {
        SpiderBuilder.Limit(limit);
        return this;
    }

    public ScraperEngineBuilder WithLinkTracker(ICrawledLinkTracker linkTracker)
    {
        SpiderBuilder.WithLinkTracker(linkTracker);
        return this;
    }

    public ScraperEngineBuilder WithLogger(ILogger logger)
    {
        SpiderBuilder.WithLogger(logger);

        Logger = logger;

        return this;
    }

    public ScraperEngineBuilder WriteToConsole()
    {
        SpiderBuilder.WriteToConsole();
        return this;
    }

    public ScraperEngineBuilder Subscribe(Action<JObject> scrapingResultHandler)
    {
        SpiderBuilder.AddSubscription(scrapingResultHandler);
        return this;
    }

    public ScraperEngineBuilder WriteToCosmosDb(
        string endpointUrl,
        string authorizationKey,
        string databaseId,
        string containerId)
    {
        SpiderBuilder.AddSink(new CosmosSink(endpointUrl, authorizationKey, databaseId, containerId, Logger));
        return this;
    }

    public ScraperEngineBuilder WriteToMongoDb(string connectionString, string databaseName, string collectionName)
    {
        SpiderBuilder.AddSink(new MongoDbSink(connectionString, databaseName, collectionName, Logger));
        return this;
    }

    public ScraperEngineBuilder WriteToCsvFile(string filePath)
    {
        SpiderBuilder.AddSink(new CsvFileSink(filePath));
        return this;
    }

    public ScraperEngineBuilder WriteToJsonFile(string filePath)
    {
        SpiderBuilder.AddSink(new JsonLinesFileSink(filePath));
        return this;
    }

    public ScraperEngineBuilder WithProxies(IProxyProvider proxyProvider)
    {
        SpiderBuilder.WithProxies(proxyProvider);
        return this;
    }

    public ScraperEngineBuilder Parse(Schema schema)
    {
        ConfigBuilder.WithScheme(schema);
        return this;
    }

    public ScraperEngineBuilder WithStartUrl(string url, PageType pageType = PageType.Static, string? initScript = null)
    {
        ConfigBuilder.WithStartUrl(url, pageType, initScript);
        return this;
    }

    public ScraperEngineBuilder FollowLinks(
        string linkSelector,
        PageType pageType = PageType.Static,
        string? script = null)
    {
        ConfigBuilder.FollowLinks(linkSelector, pageType, script);
        return this;
    }

    public ScraperEngineBuilder FollowLinks(
        string linkSelector,
        string paginationSelector,
        PageType pageType = PageType.Static,
        string? script = null)
    {
        ConfigBuilder.FollowLinks(linkSelector, paginationSelector, pageType, script);
        return this;
    }

    public ScraperEngineBuilder WithScheduler(IScheduler scheduler)
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
