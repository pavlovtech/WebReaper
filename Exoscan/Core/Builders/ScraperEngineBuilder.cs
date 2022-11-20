using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Exoscan.CookieStorage.Abstract;
using Exoscan.CookieStorage.Concrete;
using Exoscan.Domain.Parsing;
using Exoscan.LinkTracker.Abstract;
using Exoscan.LinkTracker.Concrete;
using Exoscan.Logging;
using Exoscan.PageActions;
using Exoscan.Proxy.Abstract;
using Exoscan.Scheduler.Abstract;
using Exoscan.Scheduler.Concrete;
using Exoscan.Sinks.Abstract;
using Exoscan.Sinks.Concrete;
using Exoscan.Sinks.Models;

namespace Exoscan.Core.Builders;

/// <summary>
/// Builds a web scraper engine responsible for creating and reciving crawling jobs and running a spider on them
/// </summary>
public class ScraperEngineBuilder
{
    private ConfigBuilder ConfigBuilder { get; } = new();
    private SpiderBuilder SpiderBuilder { get; } = new();

    private ILogger Logger { get; set; } = NullLogger.Instance;

    private IScheduler Scheduler { get; set; } = new InMemoryScheduler();

    protected IProxyProvider? ProxyProvider { get; set; }

    private ICookiesStorage CookieStorage { get; set; } = new InMemoryCookieStorage();
    
    public ScraperEngineBuilder AddSink(IScraperSink sink)
    {
        SpiderBuilder.AddSink(sink);
        return this;
    }

    public ScraperEngineBuilder SetCookies(Action<CookieContainer> authorize)
    {
        SpiderBuilder.SetCookies(authorize);
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

    public ScraperEngineBuilder WithLinkTracker(IVisitedLinkTracker linkTracker)
    {
        SpiderBuilder.WithLinkTracker(linkTracker);
        return this;
    }
    
    public ScraperEngineBuilder TrackVisitedLinksInFile(string fileName)
    {
        SpiderBuilder.WithLinkTracker(new FileVisitedLinkedTracker(fileName));
        return this;
    }
    
    public ScraperEngineBuilder TrackVisitedLinksInRedis(string connectionString, string redisKey)
    {
        SpiderBuilder.WithLinkTracker(new RedisVisitedLinkTracker(connectionString, redisKey));
        return this;
    }

    public ScraperEngineBuilder WithLogger(ILogger logger)
    {
        SpiderBuilder.WithLogger(logger);

        Logger = logger;

        return this;
    }

    public ScraperEngineBuilder LogToConsole()
    {
        SpiderBuilder.WithLogger(new ColorConsoleLogger());

        return this;
    }

    public ScraperEngineBuilder WriteToConsole()
    {
        SpiderBuilder.WriteToConsole();
        return this;
    }
    
    public ScraperEngineBuilder WriteToRedis(string connectionString, string redisKey)
    {
        SpiderBuilder.WriteToRedis(connectionString, redisKey);
        return this;
    }

    public ScraperEngineBuilder Subscribe(Action<ParsedData> scrapingResultHandler)
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

    public ScraperEngineBuilder Get(string url)
    {
        ConfigBuilder.Get(url);
        return this;
    }

    public ScraperEngineBuilder GetWithBrowser(
        string url,
        Func<PageActionBuilder, List<PageAction>>? actionBuilder = null)
    {
        ConfigBuilder.GetWithBrowser(url, actionBuilder?.Invoke(new()));
        return this;
    }

    public ScraperEngineBuilder Follow(string linkSelector)
    {
        ConfigBuilder.Follow(linkSelector);
        return this;
    }

    public ScraperEngineBuilder FollowWithBrowser(
        string linkSelector,
        Func<PageActionBuilder, List<PageAction>>? actionBuilder = null)
    {
        ConfigBuilder.FollowWithBrowser(linkSelector, actionBuilder?.Invoke(new()));
        return this;
    }

    public ScraperEngineBuilder Paginate(
        string linkSelector,
        string paginationSelector)
    {
        ConfigBuilder.Paginate(linkSelector, paginationSelector);
        return this;
    }

    public ScraperEngineBuilder PaginateWithBrowser(
        string linkSelector,
        string paginationSelector,
        Func<PageActionBuilder, List<PageAction>>? actionBuilder = null)
    {
        ConfigBuilder.PaginateWithBrowser(linkSelector, paginationSelector, actionBuilder?.Invoke(new()));
        return this;
    }

    public ScraperEngineBuilder WithScheduler(IScheduler scheduler)
    {
        Scheduler = scheduler;
        return this;
    }
    
    public ScraperEngineBuilder WithAzureServiceBusScheduler(string connectionString, string queueName)
    {
        Scheduler = new AzureServiceBusScheduler(connectionString, queueName);
        return this;
    }
    
    public ScraperEngineBuilder WithTextFileScheduler(string fileName, string currentJobPositionFileName)
    {
        Scheduler = new FileScheduler(fileName, currentJobPositionFileName, Logger);
        return this;
    }
    
    public ScraperEngineBuilder WithRedisScheduler(string connectionString, string queueName)
    {
        Scheduler = new RedisScheduler(connectionString, queueName, Logger);
        return this;
    }
    
    public ScraperEngineBuilder WithRedisCookieStorage(string connectionString, string redisKey)
    {
        SpiderBuilder.WithRedisCookieStorage(connectionString, redisKey);
        return this;
    }

    public ScraperEngine Build()
    {
        var config = ConfigBuilder.Build();
        var spider = SpiderBuilder.Build();

        return new ScraperEngine(config, Scheduler, spider, Logger);
    }
}
