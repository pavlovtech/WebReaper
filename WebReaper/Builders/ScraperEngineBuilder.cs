using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using WebReaper.ConfigStorage.Abstract;
using WebReaper.ConfigStorage.Concrete;
using WebReaper.Core;
using WebReaper.Core.CookieStorage.Abstract;
using WebReaper.Core.LinkTracker.Abstract;
using WebReaper.Core.LinkTracker.Concrete;
using WebReaper.Core.Scheduler.Abstract;
using WebReaper.Core.Scheduler.Concrete;
using WebReaper.Domain;
using WebReaper.Domain.PageActions;
using WebReaper.Domain.Parsing;
using WebReaper.Logging;
using WebReaper.Proxy.Abstract;
using WebReaper.Sinks.Abstract;
using WebReaper.Sinks.Concrete;
using WebReaper.Sinks.Models;

namespace WebReaper.Builders;

/// <summary>
///     Builds a web scraper engine responsible for creating and receiving crawling jobs and running a spider on them
/// </summary>
public class ScraperEngineBuilder
{
    private IVisitedLinkTracker _visitedLinksTracker = new InMemoryVisitedLinkTracker();
    private int _parallelismDegree = 20;
    private ConfigBuilder ConfigBuilder { get; } = new();
    private SpiderBuilder SpiderBuilder { get; } = new();

    private ILogger Logger { get; set; } = NullLogger.Instance;

    private IScheduler Scheduler { get; set; } = new InMemoryScheduler();
    private IScraperConfigStorage? ConfigStorage { get; set; } = new InMemoryScraperConfigStorage();
    
    protected IProxyProvider? ProxyProvider { get; set; }

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

    public ScraperEngineBuilder PageCrawlLimit(int limit)
    {
        ConfigBuilder.WithPageCrawlLimit(limit);
        return this;
    }

    public ScraperEngineBuilder HeadlessMode(bool headless)
    {
        ConfigBuilder.HeadlessMode(headless);
        return this;
    }

    public ScraperEngineBuilder WithLinkTracker(IVisitedLinkTracker linkTracker)
    {
        SpiderBuilder.WithLinkTracker(linkTracker);
        return this;
    }

    public ScraperEngineBuilder TrackVisitedLinksInFile(string fileName, bool dataCleanupOnStart = false)
    {
        _visitedLinksTracker = new FileVisitedLinkedTracker(fileName, dataCleanupOnStart);
        SpiderBuilder.WithLinkTracker(_visitedLinksTracker);
        return this;
    }

    public ScraperEngineBuilder TrackVisitedLinksInRedis(string connectionString, string redisKey, bool dataCleanupOnStart = false)
    {
        _visitedLinksTracker = new RedisVisitedLinkTracker(connectionString, redisKey, dataCleanupOnStart);
        SpiderBuilder.WithLinkTracker(_visitedLinksTracker);
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


    public ScraperEngineBuilder WriteToRedis(
        string connectionString,
        string redisKey,
        bool dataCleanupOnStart = false)
    {
        SpiderBuilder.WriteToRedis(connectionString, redisKey, dataCleanupOnStart);
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
        string containerId,
        bool dataCleanupOnStart)
    {
        //SpiderBuilder.AddSink(new CosmosSink(endpointUrl, authorizationKey, databaseId, containerId, _dataCleanupOnStart, Logger));
        SpiderBuilder.WriteToCosmosDb(endpointUrl, authorizationKey, databaseId, containerId, dataCleanupOnStart);
        return this;
    }

    public ScraperEngineBuilder WriteToMongoDb(
        string connectionString,
        string databaseName,
        string collectionName,
        bool dataCleanupOnStart)
    {
        SpiderBuilder.AddSink(new MongoDbSink(connectionString, databaseName, collectionName, dataCleanupOnStart,
            Logger));
        return this;
    }

    public ScraperEngineBuilder WriteToCsvFile(string filePath, bool dataCleanupOnStart)
    {
        SpiderBuilder.WriteToCsvFile(filePath, dataCleanupOnStart);
        return this;
    }

    public ScraperEngineBuilder WriteToJsonFile(string filePath, bool dataCleanupOnStart)
    {
        SpiderBuilder.WriteToJsonFile(filePath, dataCleanupOnStart);
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

    public ScraperEngineBuilder Get(params string[] startUrls)
    {
        ConfigBuilder.Get(startUrls);
        return this;
    }

    public ScraperEngineBuilder GetWithBrowser(
        IEnumerable<string> startUrls,
        Func<PageActionBuilder, List<PageAction>>? actionBuilder = null)
    {
        ConfigBuilder.GetWithBrowser(startUrls, actionBuilder?.Invoke(new PageActionBuilder()));
        return this;
    }

    public ScraperEngineBuilder Follow(string linkSelector)
    {
        ConfigBuilder.Follow(linkSelector);
        return this;
    }

    public ScraperEngineBuilder FollowWithBrowser(
        string linkSelector,
        Func<PageActionBuilder, 
        List<PageAction>>? actionBuilder = null)
    {
        ConfigBuilder.FollowWithBrowser(linkSelector, actionBuilder?.Invoke(new PageActionBuilder()));
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
        ConfigBuilder.PaginateWithBrowser(linkSelector, paginationSelector,
            actionBuilder?.Invoke(new PageActionBuilder()));
        return this;
    }

    public ScraperEngineBuilder WithScheduler(IScheduler scheduler)
    {
        Scheduler = scheduler;
        return this;
    }

    public ScraperEngineBuilder WithAzureServiceBusScheduler(
        string connectionString,
        string queueName,
        bool dataCleanupOnStart = false)
    {
        Scheduler = new AzureServiceBusScheduler(connectionString, queueName, dataCleanupOnStart);
        return this;
    }

    public ScraperEngineBuilder WithTextFileScheduler(
        string fileName,
        string currentJobPositionFileName,
        bool dataCleanupOnStart = false)
    {
        Scheduler = new FileScheduler(fileName, currentJobPositionFileName, Logger, dataCleanupOnStart);
        return this;
    }

    public ScraperEngineBuilder WithRedisScheduler(
        string connectionString,
        string queueName,
        bool dataCleanupOnStart = false)
    {
        Scheduler = new RedisScheduler(connectionString, queueName, Logger, dataCleanupOnStart);
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

    public ScraperEngineBuilder WithMongoDbCookieStorage(string connectionString, string databaseName,
        string collectionName, string cookieCollectionId, ILogger logger)
    {
        SpiderBuilder.WithMongoDbCookieStorage(connectionString, databaseName, collectionName, cookieCollectionId,
            logger);
        return this;
    }
    
    public ScraperEngineBuilder WithFileCookieStorage(string fileName)
    {
        SpiderBuilder.WithFileCookieStorage(fileName);
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
        ConfigStorage =
            new MongoDbScraperConfigStorage(connectionString, databaseName, collectionName, configId, Logger);
        SpiderBuilder.WithMongoDbConfigStorage(connectionString, databaseName, collectionName, configId, Logger);

        return this;
    }

    public ScraperEngineBuilder PostProcess(Func<Metadata, JObject, Task> action)
    {
        SpiderBuilder.PostProcess(action);
        return this;
    }

    public ScraperEngineBuilder WithParallelismDegree(int parallelismDegree)
    {
        _parallelismDegree = parallelismDegree;
        return this;
    }

    public async Task<ScraperEngine> BuildAsync()
    {
        SpiderBuilder.WithConfigStorage(ConfigStorage);
        
        var config = ConfigBuilder.Build();
        var spider = SpiderBuilder.Build();
        
        await ConfigStorage.CreateConfigAsync(config);

        return new ScraperEngine(_parallelismDegree, ConfigStorage, Scheduler, spider, Logger);
    }
}