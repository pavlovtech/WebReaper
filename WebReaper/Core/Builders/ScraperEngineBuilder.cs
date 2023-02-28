using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using WebReaper.ConfigStorage.Abstract;
using WebReaper.ConfigStorage.Concrete;
using WebReaper.CookieStorage.Abstract;
using WebReaper.CookieStorage.Concrete;
using WebReaper.Domain;
using WebReaper.Domain.Parsing;
using WebReaper.LinkTracker.Abstract;
using WebReaper.LinkTracker.Concrete;
using WebReaper.Logging;
using WebReaper.PageActions;
using WebReaper.Proxy.Abstract;
using WebReaper.Scheduler.Abstract;
using WebReaper.Scheduler.Concrete;
using WebReaper.Sinks.Abstract;
using WebReaper.Sinks.Concrete;
using WebReaper.Sinks.Models;

namespace WebReaper.Core.Builders;

/// <summary>
/// Builds a web scraper engine responsible for creating and receiving crawling jobs and running a spider on them
/// </summary>
public class ScraperEngineBuilder
{
    private ConfigBuilder ConfigBuilder { get; } = new();
    private SpiderBuilder SpiderBuilder { get; } = new();

    private ILogger Logger { get; set; } = NullLogger.Instance;

    private IScheduler Scheduler { get; set; } = new InMemoryScheduler();

    protected IProxyProvider? ProxyProvider { get; set; }

    private ICookiesStorage CookieStorage { get; set; } = new InMemoryCookieStorage();

    private IScraperConfigStorage? ConfigStorage { get; set; }

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
        ConfigBuilder.IgnoreUrls(urls);
        return this;
    }

    public ScraperEngineBuilder Limit(int limit)
    {
        ConfigBuilder.WithPageCrawlLimit(limit);
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
    
    public ScraperEngineBuilder WithCookieStorage(ICookiesStorage cookiesStorage)
    {
        SpiderBuilder.WithCookieStorage(cookiesStorage);
        return this;
    }
    
    public ScraperEngineBuilder WithRedisCookieStorage(string connectionString, string redisKey)
    {
        SpiderBuilder.WithRedisCookieStorage(connectionString, redisKey);
        return this;
    }
    
    public ScraperEngineBuilder WithMongoDbCookieStorage(string connectionString, string databaseName, string collectionName, string cookieCollectionId, ILogger logger)
    {
        SpiderBuilder.WithMongoDbCookieStorage(connectionString, databaseName, collectionName, cookieCollectionId, logger);
        return this;
    }

    public ScraperEngineBuilder WithConfigStorage(IScraperConfigStorage configStorage)
    {
        ConfigStorage = configStorage;
        return this;
    }

    public ScraperEngineBuilder WithFileConfigStorage(string fileName)
    {
        ConfigStorage = new FileScraperConfigStorage(fileName);
        SpiderBuilder.WithFileConfigStorage(fileName);

        return this;
    }
    
    public ScraperEngineBuilder WithRedisConfigStorage(string connectionString, string redisKey)
    {
        ConfigStorage = new RedisScraperConfigStorage(connectionString, redisKey, Logger);
        SpiderBuilder.WithRedisConfigStorage(connectionString, redisKey);
        
        return this;
    }
    
    public ScraperEngineBuilder WithMongoDbConfigStorage(
        string connectionString,
        string databaseName,
        string collectionName,
        string configId)
    {
        ConfigStorage = new MongoDbScraperConfigStorage(connectionString, databaseName, collectionName, configId, Logger);
        SpiderBuilder.WithMongoDbConfigStorage(connectionString, databaseName, collectionName, configId, Logger);
        
        return this;
    }
    
    public ScraperEngineBuilder PostProcess(Func<Metadata, JObject, Task> action)
    {
        SpiderBuilder.PostProcess(action);
        return this;
    }

    public ScraperEngine Build()
    {
        ConfigStorage ??= new InMemoryScraperConfigStorage();
        
        SpiderBuilder.WithConfigStorage(ConfigStorage);

        var config = ConfigBuilder.Build();
        var spider = SpiderBuilder.Build();

        return new ScraperEngine(config, ConfigStorage, Scheduler, spider, Logger);
    }
}
