using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json.Nodes;
using WebReaper.ConfigStorage.Abstract;
using WebReaper.ConfigStorage.Concrete;
using WebReaper.Core;
using WebReaper.Core.CookieStorage.Abstract;
using WebReaper.Core.LinkTracker.Abstract;
using WebReaper.Core.LinkTracker.Concrete;
using WebReaper.Core.Loaders.Abstract;
using WebReaper.Core.Parser.Abstract;
using WebReaper.Core.Scheduler.Abstract;
using WebReaper.Core.Scheduler.Concrete;
using WebReaper.Core.Spider.Abstract;
using WebReaper.Domain;
using WebReaper.Domain.PageActions;
using WebReaper.Domain.Parsing;
using WebReaper.Logging;
using WebReaper.Proxy;
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

    public ScraperEngineBuilder WithContentParser(IJsonContentParser contentParser)
    {
        SpiderBuilder.WithContentParser(contentParser);
        return this;
    }

    /// <summary>
    /// Parse responses as JSON instead of HTML (issue #27). Schema
    /// selectors become JSONPath expressions.
    /// </summary>
    public ScraperEngineBuilder WithJsonContentParser()
    {
        SpiderBuilder.WithJsonContentParser();
        return this;
    }

    /// <summary>
    /// Parse responses as HTML with XPath 1.0 selectors instead of CSS
    /// (discussion #17). Same shared Schema fold, different backend
    /// (ADR 0002 / 0007).
    /// </summary>
    public ScraperEngineBuilder WithXPathContentParser()
    {
        SpiderBuilder.WithXPathContentParser();
        return this;
    }

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

    /// <summary>
    /// Stop the engine once all discovered links have been crawled
    /// (issue #20), so <c>await engine.RunAsync()</c> actually returns
    /// for finite crawls. Uses the in-memory scheduler.
    /// </summary>
    public ScraperEngineBuilder StopWhenAllLinksProcessed()
    {
        ConfigBuilder.StopWhenAllLinksProcessed();
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


    public ScraperEngineBuilder Subscribe(Action<ParsedData> scrapingResultHandler)
    {
        SpiderBuilder.AddSubscription(scrapingResultHandler);
        return this;
    }

    public ScraperEngineBuilder WriteToCsvFile(string filePath, bool dataCleanupOnStart)
    {
        SpiderBuilder.WriteToCsvFile(filePath, dataCleanupOnStart);
        return this;
    }

    public ScraperEngineBuilder WriteToJsonFile(string filePath, bool dataCleanupOnStart = true)
    {
        SpiderBuilder.WriteToJsonFile(filePath, dataCleanupOnStart);
        return this;
    }

    public ScraperEngineBuilder WithProxies(IProxyProvider proxyProvider)
    {
        SpiderBuilder.WithProxies(proxyProvider);
        return this;
    }

    /// <summary>
    /// Register the transport used for Dynamic (headless-browser) pages
    /// (ADR-0009). Core is HTTP-only by default; the headless-browser
    /// transport lives in the WebReaper.Puppeteer satellite — add that
    /// package and call <c>.WithPuppeteerPageLoader()</c> (which calls this
    /// seam). The factory is invoked at build time with the builder's
    /// resolved cookie storage, optional proxy provider and logger, so the
    /// pre-7.0 default behaviour is preserved exactly. ADR-0004's
    /// one-<see cref="IPageLoader"/> / two-<see cref="IPageLoadTransport"/>
    /// dispatcher is unchanged.
    /// </summary>
    public ScraperEngineBuilder WithLoadTransport(
        Func<ICookiesStorage, IProxyProvider?, ILogger, IPageLoadTransport> dynamicTransportFactory)
    {
        SpiderBuilder.WithLoadTransport(dynamicTransportFactory);
        return this;
    }

    public ScraperEngineBuilder WithValidatedProxies(
        IProxySource source,
        IEnumerable<IProxyValidator> validators,
        ValidatedProxyProviderOptions? options = null)
    {
        SpiderBuilder.WithValidatedProxies(source, validators, options);
        return this;
    }

    public ScraperEngineBuilder WithValidatedProxies(IProxySource source, params IProxyValidator[] validators)
    {
        SpiderBuilder.WithValidatedProxies(source, validators);
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
    public ScraperEngineBuilder GetWithBrowser(params string[] startUrls)
    {
        ConfigBuilder.GetWithBrowser(startUrls);
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

    public ScraperEngineBuilder WithTextFileScheduler(
        string fileName,
        string currentJobPositionFileName,
        bool dataCleanupOnStart = false)
    {
        Scheduler = new FileScheduler(fileName, currentJobPositionFileName, Logger, dataCleanupOnStart);
        return this;
    }

    public ScraperEngineBuilder WithCookieStorage(ICookiesStorage cookiesStorage)
    {
        SpiderBuilder.WithCookieStorage(cookiesStorage);
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

    public ScraperEngineBuilder PostProcess(Func<Metadata, JsonObject, Task> action)
    {
        SpiderBuilder.PostProcess(action);
        return this;
    }

    public ScraperEngineBuilder WithParallelismDegree(int parallelismDegree)
    {
        _parallelismDegree = parallelismDegree;
        return this;
    }

    /// <summary>
    /// Build just the configured <see cref="ISpider"/> — no scheduler, no
    /// engine loop, and (unlike <see cref="BuildAsync"/>) no
    /// <c>ConfigBuilder.Build()</c>/persist, so it does <em>not</em> require
    /// <c>Get</c>/<c>Parse</c>. This is the seam for the distributed-worker
    /// pattern (ADR-0009): pull one <c>Job</c> off your own queue, crawl it
    /// with <c>spider.CrawlAsync(job)</c>, and re-enqueue the returned child
    /// jobs yourself — see <c>Examples/WebReaper.AzureFuncs</c>. The spider
    /// reads its <see cref="Domain.ScraperConfig"/> from the configured
    /// config storage at crawl time, so persist it separately (e.g. a
    /// distributed config storage written by a start-scraping endpoint).
    /// </summary>
    public ISpider BuildSpider()
    {
        SpiderBuilder.WithConfigStorage(ConfigStorage);
        return SpiderBuilder.Build();
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
