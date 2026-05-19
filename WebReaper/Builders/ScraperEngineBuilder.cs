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
/// The public fluent façade for configuring and building a scraper. It
/// composes a <see cref="ConfigBuilder"/> (the immutable
/// <see cref="ScraperConfig"/>: start URLs, the selector chain, the crawl
/// limit) and an internal <c>SpiderBuilder</c> (runtime components — loaders,
/// parsers, sinks, trackers, cookie storage), all with in-memory defaults.
/// Terminate the chain with <see cref="BuildAsync"/> (an engine plus its
/// persisted config) or <see cref="BuildSpider"/> (a bare
/// <see cref="ISpider"/> for the distributed-worker pattern, ADR-0009). Every
/// method returns the same builder for chaining; configuration order is free
/// except that <see cref="BuildAsync"/> requires a start set
/// (<see cref="Get"/> / <c>GetWithBrowser</c>) and a schema
/// (<see cref="Parse"/>).
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
    /// <summary>The proxy provider, once configured via
    /// <see cref="WithProxies"/> / <see cref="WithValidatedProxies(IProxySource, IEnumerable{IProxyValidator}, ValidatedProxyProviderOptions)"/>.</summary>
    protected IProxyProvider? ProxyProvider { get; set; }

    /// <summary>Use a custom content parser (the Schema-fold backend, ADR-0002)
    /// instead of the default AngleSharp/CSS one.</summary>
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

    /// <summary>Add a custom <see cref="IScraperSink"/>. Sinks compose — every
    /// registered sink receives every scraped record.</summary>
    public ScraperEngineBuilder AddSink(IScraperSink sink)
    {
        SpiderBuilder.AddSink(sink);
        return this;
    }

    /// <summary>Seed the crawl's cookie container (e.g. an authenticated
    /// session) before any page loads — see <see cref="ICookiesStorage"/>.</summary>
    public ScraperEngineBuilder SetCookies(Action<CookieContainer> authorize)
    {
        SpiderBuilder.SetCookies(authorize);
        return this;
    }

    /// <summary>URLs the crawl must never enqueue (a discovery-time
    /// blocklist).</summary>
    public ScraperEngineBuilder IgnoreUrls(params string[] urls)
    {
        ConfigBuilder.IgnoreUrls(urls);
        return this;
    }

    /// <summary>Soft cap on pages crawled (ADR-0022: best-effort — in-flight
    /// pages still finish, so it can overshoot by ~the parallelism degree).</summary>
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

    /// <summary>Run the browser headed (<c>false</c>) or headless
    /// (<c>true</c>, default) for dynamic pages.</summary>
    public ScraperEngineBuilder HeadlessMode(bool headless)
    {
        ConfigBuilder.HeadlessMode(headless);
        return this;
    }

    /// <summary>Use a custom <see cref="IVisitedLinkTracker"/> (the
    /// idempotency authority, ADR-0022) instead of the in-memory default.</summary>
    public ScraperEngineBuilder WithLinkTracker(IVisitedLinkTracker linkTracker)
    {
        SpiderBuilder.WithLinkTracker(linkTracker);
        return this;
    }

    /// <summary>
    /// Track visited links in a file so a crawl resumes across restarts.
    /// </summary>
    /// <param name="fileName">Path of the visited-links file.</param>
    /// <param name="dataCleanupOnStart">Wipe the file on start (fresh run)
    /// rather than resume; defaults to <c>false</c> (resume).</param>
    /// <exception cref="ArgumentException"><paramref name="fileName"/> is
    /// null/empty/whitespace (8.0.0 fail-fast).</exception>
    public ScraperEngineBuilder TrackVisitedLinksInFile(string fileName, bool dataCleanupOnStart = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        _visitedLinksTracker = new FileVisitedLinkedTracker(fileName, dataCleanupOnStart);
        SpiderBuilder.WithLinkTracker(_visitedLinksTracker);
        return this;
    }

    /// <summary>Route library logging to your <see cref="ILogger"/>.</summary>
    public ScraperEngineBuilder WithLogger(ILogger logger)
    {
        SpiderBuilder.WithLogger(logger);

        Logger = logger;

        return this;
    }

    /// <summary>Log to the console with the built-in colour logger.</summary>
    public ScraperEngineBuilder LogToConsole()
    {
        SpiderBuilder.WithLogger(new ColorConsoleLogger());

        return this;
    }

    /// <summary>Write every scraped record to the console (a built-in
    /// <see cref="IScraperSink"/>).</summary>
    public ScraperEngineBuilder WriteToConsole()
    {
        SpiderBuilder.WriteToConsole();
        return this;
    }


    /// <summary>
    /// Invoke <paramref name="scrapingResultHandler"/> for every scraped
    /// record. ADR-0022: the callback is wired onto the Crawl driver; this
    /// builder surface is unchanged.
    /// </summary>
    public ScraperEngineBuilder Subscribe(Action<ParsedData> scrapingResultHandler)
    {
        SpiderBuilder.AddSubscription(scrapingResultHandler);
        return this;
    }

    /// <summary>
    /// Write scraped records to a CSV file.
    /// </summary>
    /// <param name="filePath">Path of the CSV file.</param>
    /// <param name="dataCleanupOnStart">Wipe the file on start vs append.
    /// Required — no default (contrast <see cref="WriteToJsonFile"/>, whose
    /// default is <c>true</c>).</param>
    /// <exception cref="ArgumentException"><paramref name="filePath"/> is
    /// null/empty/whitespace.</exception>
    public ScraperEngineBuilder WriteToCsvFile(string filePath, bool dataCleanupOnStart)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        SpiderBuilder.WriteToCsvFile(filePath, dataCleanupOnStart);
        return this;
    }

    /// <summary>
    /// Write scraped records to a file as <b>JSON Lines</b> — one JSON object
    /// per line, not a JSON array.
    /// </summary>
    /// <param name="filePath">Path of the JSON Lines file.</param>
    /// <param name="dataCleanupOnStart">Wipe the file on start. Defaults to
    /// <c>true</c> (fresh file each run) — the opposite of the other file
    /// sinks; pass <c>false</c> to append across runs.</param>
    /// <exception cref="ArgumentException"><paramref name="filePath"/> is
    /// null/empty/whitespace.</exception>
    public ScraperEngineBuilder WriteToJsonFile(string filePath, bool dataCleanupOnStart = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        SpiderBuilder.WriteToJsonFile(filePath, dataCleanupOnStart);
        return this;
    }

    /// <summary>Route page loads through a rotating proxy
    /// (<see cref="IProxyProvider"/>).</summary>
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

    /// <summary>Rotate over proxies from <paramref name="source"/>, keeping
    /// only those every <paramref name="validators"/> approves.</summary>
    public ScraperEngineBuilder WithValidatedProxies(
        IProxySource source,
        IEnumerable<IProxyValidator> validators,
        ValidatedProxyProviderOptions? options = null)
    {
        SpiderBuilder.WithValidatedProxies(source, validators, options);
        return this;
    }

    /// <summary><see cref="WithValidatedProxies(IProxySource, IEnumerable{IProxyValidator}, ValidatedProxyProviderOptions)"/>
    /// with the validators as params and default options.</summary>
    public ScraperEngineBuilder WithValidatedProxies(IProxySource source, params IProxyValidator[] validators)
    {
        SpiderBuilder.WithValidatedProxies(source, validators);
        return this;
    }

    /// <summary>The extraction <see cref="Schema"/> for target pages (the
    /// fold grammar, ADR-0002). Required by <see cref="BuildAsync"/>.</summary>
    public ScraperEngineBuilder Parse(Schema schema)
    {
        ConfigBuilder.WithScheme(schema);
        return this;
    }

    /// <summary>The crawl's start URLs, loaded as static HTTP pages. Required
    /// by <see cref="BuildAsync"/> (or use <see cref="GetWithBrowser(string[])"/>).</summary>
    public ScraperEngineBuilder Get(params string[] startUrls)
    {
        ConfigBuilder.Get(startUrls);
        return this;
    }

    /// <summary>
    /// Start URLs loaded through the headless browser, with an optional
    /// <see cref="PageActionBuilder"/> per page. Requires the
    /// WebReaper.Puppeteer satellite (ADR-0009).
    /// </summary>
    public ScraperEngineBuilder GetWithBrowser(
        IEnumerable<string> startUrls,
        Func<PageActionBuilder, List<PageAction>>? actionBuilder = null)
    {
        ConfigBuilder.GetWithBrowser(startUrls, actionBuilder?.Invoke(new PageActionBuilder()));
        return this;
    }

    /// <summary>Start URLs loaded through the headless browser, no page
    /// actions. Requires the WebReaper.Puppeteer satellite.</summary>
    public ScraperEngineBuilder GetWithBrowser(params string[] startUrls)
    {
        ConfigBuilder.GetWithBrowser(startUrls);
        return this;
    }

    /// <summary>Append a follow step over <paramref name="linkSelector"/> (one
    /// link in the crawl's selector chain, ADR-0001).</summary>
    public ScraperEngineBuilder Follow(string linkSelector)
    {
        ConfigBuilder.Follow(linkSelector);
        return this;
    }

    /// <summary>
    /// <see cref="Follow"/> where the followed pages load via the headless
    /// browser, with optional page actions. Requires the WebReaper.Puppeteer
    /// satellite.
    /// </summary>
    public ScraperEngineBuilder FollowWithBrowser(
        string linkSelector,
        Func<PageActionBuilder,
        List<PageAction>>? actionBuilder = null)
    {
        ConfigBuilder.FollowWithBrowser(linkSelector, actionBuilder?.Invoke(new PageActionBuilder()));
        return this;
    }

    /// <summary>Append a paginate step: <paramref name="linkSelector"/> applied
    /// across pages walked via <paramref name="paginationSelector"/>
    /// (ADR-0001).</summary>
    public ScraperEngineBuilder Paginate(
        string linkSelector,
        string paginationSelector)
    {
        ConfigBuilder.Paginate(linkSelector, paginationSelector);
        return this;
    }

    /// <summary>
    /// <see cref="Paginate"/> where the pages load via the headless browser,
    /// with optional page actions. Requires the WebReaper.Puppeteer satellite.
    /// </summary>
    public ScraperEngineBuilder PaginateWithBrowser(
        string linkSelector,
        string paginationSelector,
        Func<PageActionBuilder, List<PageAction>>? actionBuilder = null)
    {
        ConfigBuilder.PaginateWithBrowser(linkSelector, paginationSelector,
            actionBuilder?.Invoke(new PageActionBuilder()));
        return this;
    }

    /// <summary>Use a custom <see cref="IScheduler"/> (e.g. a distributed
    /// queue) instead of the in-memory default.</summary>
    public ScraperEngineBuilder WithScheduler(IScheduler scheduler)
    {
        Scheduler = scheduler;
        return this;
    }

    /// <summary>
    /// Persist the job queue and its cursor to files so a crawl resumes across
    /// restarts (the file scheduler).
    /// </summary>
    /// <param name="fileName">Path of the job-queue file.</param>
    /// <param name="currentJobPositionFileName">Path of the cursor file
    /// tracking how far the queue has been consumed.</param>
    /// <param name="dataCleanupOnStart">Wipe the files on start vs resume;
    /// defaults to <c>false</c> (resume).</param>
    /// <exception cref="ArgumentException">either file name is
    /// null/empty/whitespace.</exception>
    public ScraperEngineBuilder WithTextFileScheduler(
        string fileName,
        string currentJobPositionFileName,
        bool dataCleanupOnStart = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentJobPositionFileName);
        Scheduler = new FileScheduler(fileName, currentJobPositionFileName, Logger, dataCleanupOnStart);
        return this;
    }

    /// <summary>Use a custom <see cref="ICookiesStorage"/> instead of the
    /// in-memory default.</summary>
    public ScraperEngineBuilder WithCookieStorage(ICookiesStorage cookiesStorage)
    {
        SpiderBuilder.WithCookieStorage(cookiesStorage);
        return this;
    }

    /// <summary>Persist cookies to a file so an authenticated session
    /// survives restarts.</summary>
    /// <exception cref="ArgumentException"><paramref name="fileName"/> is
    /// null/empty/whitespace.</exception>
    public ScraperEngineBuilder WithFileCookieStorage(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        SpiderBuilder.WithFileCookieStorage(fileName);
        return this;
    }

    /// <summary>Use a custom <see cref="IScraperConfigStorage"/> (e.g. a
    /// distributed store shared with workers) instead of the in-memory
    /// default.</summary>
    public ScraperEngineBuilder WithConfigStorage(IScraperConfigStorage configStorage)
    {
        ConfigStorage = configStorage;
        return this;
    }

    /// <summary>Persist the <see cref="ScraperConfig"/> to a file.</summary>
    /// <exception cref="ArgumentException"><paramref name="fileName"/> is
    /// null/empty/whitespace.</exception>
    public ScraperEngineBuilder WithFileConfigStorage(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ConfigStorage = new FileScraperConfigStorage(fileName);
        SpiderBuilder.WithFileConfigStorage(fileName);

        return this;
    }

    /// <summary>
    /// Post-process every scraped record (e.g. enrich or reshape it) before it
    /// reaches the sinks. ADR-0022: wired onto the Crawl driver; this surface
    /// is unchanged.
    /// </summary>
    public ScraperEngineBuilder PostProcess(Func<Metadata, JsonObject, Task> action)
    {
        SpiderBuilder.PostProcess(action);
        return this;
    }

    /// <summary>Degree of parallelism for the crawl loop (concurrent
    /// in-flight pages). Default 20.</summary>
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

    /// <summary>
    /// Build the engine: validate and persist the <see cref="ScraperConfig"/>
    /// to the configured <see cref="IScraperConfigStorage"/>, construct the
    /// <see cref="ISpider"/>, and return a runnable <see cref="ScraperEngine"/>
    /// (the in-process Crawl driver, ADR-0022).
    /// </summary>
    /// <exception cref="InvalidOperationException">no start URLs
    /// (<see cref="Get"/>/<see cref="GetWithBrowser(string[])"/>) or no schema
    /// (<see cref="Parse"/>) was configured.</exception>
    public async Task<ScraperEngine> BuildAsync()
    {
        SpiderBuilder.WithConfigStorage(ConfigStorage);
        var config = ConfigBuilder.Build();
        var spider = SpiderBuilder.Build();
        await ConfigStorage.CreateConfigAsync(config);

        return new ScraperEngine(
            _parallelismDegree, ConfigStorage, Scheduler, spider,
            SpiderBuilder.DriverLinkTracker, SpiderBuilder.DriverSinks, Logger,
            SpiderBuilder.DriverScrapedData, SpiderBuilder.DriverPostProcessor);
    }
}
