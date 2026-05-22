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
using WebReaper.Infra.Abstract;
using WebReaper.Logging;
using WebReaper.Processing;
using WebReaper.Processing.Abstract;
using WebReaper.Processing.Concrete;
using WebReaper.Proxy;
using WebReaper.Proxy.Abstract;
using WebReaper.Sinks.Abstract;
using WebReaper.Sinks.Concrete;
using WebReaper.Sinks.Models;

namespace WebReaper.Builders;

/// <summary>
/// The public fluent builder for configuring and building a scraper engine
/// (ADR-0025). A scrape begins with a <b>Crawl seed</b>: obtain one from the
/// static <see cref="Crawl(string[])"/> / <see cref="CrawlWithBrowser(string[])"/>,
/// give it a <see cref="Schema"/> via <see cref="ICrawlSeed.Extract"/>, and
/// you get this builder — every free fluent method and every satellite
/// extension — terminating in <see cref="BuildAsync"/> (a runnable engine
/// plus its persisted config) or <see cref="Build"/> (just the immutable
/// <see cref="ScraperConfig"/>, e.g. for a distributed start endpoint to
/// persist). The constructor is internal and the terminals live only here, so
/// "build with no start URLs or no schema" is unrepresentable. The
/// distributed-worker reduced shell (ADR-0009) is a separate seam —
/// <see cref="DistributedSpiderBuilder"/>. Composes an internal
/// <c>ConfigBuilder</c> and <c>SpiderBuilder</c>, all with in-memory defaults.
/// </summary>
public class ScraperEngineBuilder
{
    private IVisitedLinkTracker _visitedLinksTracker = new InMemoryVisitedLinkTracker();
    private int _parallelismDegree = 20;
    private ConfigBuilder ConfigBuilder { get; } = new();
    private SpiderBuilder SpiderBuilder { get; } = new();

    /// <summary>Internal: a <see cref="ScraperEngineBuilder"/> is obtained only
    /// via <see cref="Crawl(string[])"/> / <see cref="CrawlWithBrowser(string[])"/>
    /// then <see cref="ICrawlSeed.Extract"/> — never <c>new</c>ed. This is the
    /// structural guarantee (ADR-0025): the build terminals cannot be reached
    /// without a Crawl seed and a schema.</summary>
    internal ScraperEngineBuilder() { }

    /// <summary>
    /// Begin a scrape: the crawl's start URLs, loaded as static HTTP pages
    /// (the ADR-0025 Crawl seed). Returns an <see cref="ICrawlSeed"/> whose
    /// only operation is <see cref="ICrawlSeed.Extract"/>.
    /// </summary>
    /// <exception cref="ArgumentException">no start URL was supplied
    /// (fail-fast).</exception>
    public static ICrawlSeed Crawl(params string[] startUrls)
    {
        if (startUrls is null || startUrls.Length == 0)
            throw new ArgumentException(
                "At least one start URL is required.", nameof(startUrls));
        var builder = new ScraperEngineBuilder();
        builder.ConfigBuilder.Get(startUrls);
        return new CrawlSeed(builder);
    }

    /// <summary>
    /// Begin a scrape whose start pages load through the headless-browser
    /// transport (the ADR-0025 Crawl seed; requires the WebReaper.Puppeteer
    /// satellite at run time, ADR-0009).
    /// </summary>
    /// <exception cref="ArgumentException">no start URL was supplied
    /// (fail-fast).</exception>
    public static ICrawlSeed CrawlWithBrowser(params string[] startUrls)
    {
        if (startUrls is null || startUrls.Length == 0)
            throw new ArgumentException(
                "At least one start URL is required.", nameof(startUrls));
        var builder = new ScraperEngineBuilder();
        builder.ConfigBuilder.GetWithBrowser(startUrls);
        return new CrawlSeed(builder);
    }

    /// <summary>
    /// <see cref="CrawlWithBrowser(string[])"/> with an optional
    /// <see cref="PageActionBuilder"/> run on each start page before scraping.
    /// </summary>
    /// <exception cref="ArgumentException">no start URL was supplied
    /// (fail-fast).</exception>
    public static ICrawlSeed CrawlWithBrowser(
        IEnumerable<string> startUrls,
        Func<PageActionBuilder, List<PageAction>>? actionBuilder = null)
    {
        var urls = startUrls?.ToArray() ?? Array.Empty<string>();
        if (urls.Length == 0)
            throw new ArgumentException(
                "At least one start URL is required.", nameof(startUrls));
        var builder = new ScraperEngineBuilder();
        builder.ConfigBuilder.GetWithBrowser(urls, actionBuilder?.Invoke(new PageActionBuilder()));
        return new CrawlSeed(builder);
    }

    /// <summary>The <see cref="ICrawlSeed"/> implementation: holds the
    /// half-built builder so the only operation reachable after
    /// <see cref="Crawl(string[])"/> is <see cref="ICrawlSeed.Extract"/>.</summary>
    private sealed class CrawlSeed : ICrawlSeed
    {
        private readonly ScraperEngineBuilder _builder;
        internal CrawlSeed(ScraperEngineBuilder builder) => _builder = builder;

        public ScraperEngineBuilder Extract(Schema schema)
        {
            _builder.ConfigBuilder.WithScheme(schema);
            return _builder;
        }
    }

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
    /// record. ADR-0038: sugar for registering a delegate
    /// <see cref="IScraperSink"/> — an in-process delegate destination, not a
    /// separate notification seam; it composes with the other sinks and
    /// receives its own clone of the record, like any sink. To react to a page
    /// <em>before</em> it reaches the sinks (enrich / filter / repair), use
    /// <see cref="Process(IPageProcessor)"/> instead.
    /// </summary>
    public ScraperEngineBuilder Subscribe(Action<ParsedData> scrapingResultHandler)
    {
        SpiderBuilder.AddSink(new DelegateSink(scrapingResultHandler));
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

    /// <summary>
    /// Replace the Crawl driver's retry around the per-Job Spider call
    /// (ADR-0026). The default is the internal fixed-attempts policy — four
    /// attempts total (one initial + three retries), no delay between them,
    /// every exception except <see cref="OperationCanceledException"/>
    /// triggers a retry. Supply your own <see cref="IRetryPolicy"/> for
    /// exponential backoff (e.g. wrapping a Polly resilience pipeline), to
    /// disable retries in tests, or for a satellite-aware policy. Cancellation
    /// is always cooperative — implementations must propagate
    /// <see cref="OperationCanceledException"/> without retrying.
    /// </summary>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="retryPolicy"/> is null.</exception>
    public ScraperEngineBuilder WithRetryPolicy(IRetryPolicy retryPolicy)
    {
        SpiderBuilder.WithRetryPolicy(retryPolicy);
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

        return this;
    }

    /// <summary>
    /// Append a page processor to the pipeline (ADR-0038). Processors run in
    /// registration order over each crawled target page's extracted record,
    /// after the Schema fold and before the Sink fan-out — enrich, observe,
    /// filter, or repair. Implement <see cref="IPageProcessor"/> for a
    /// processor that holds state (e.g. an LLM client) or needs async warm-up.
    /// </summary>
    public ScraperEngineBuilder Process(IPageProcessor processor)
    {
        SpiderBuilder.AddProcessor(processor);
        return this;
    }

    /// <summary>
    /// Append a page processor expressed as a delegate (ADR-0038) — for a
    /// stateless stage that needs no class. Return <see cref="PageVerdict.Keep"/>
    /// to carry a record forward (enrich / observe / repair) or
    /// <see cref="PageVerdict.Drop"/> to filter the page out so no sink emits it.
    /// </summary>
    public ScraperEngineBuilder Process(
        Func<PageContext, CancellationToken, ValueTask<PageVerdict>> processor)
    {
        SpiderBuilder.AddProcessor(new DelegatePageProcessor(processor));
        return this;
    }

    /// <summary>
    /// Append a trivial synchronous enrich stage (ADR-0038): the action mutates
    /// the extracted record's JSON in place (add or change fields) and the
    /// record is kept. The dead-common page-processor case — no
    /// <see cref="IPageProcessor"/> class and no <see cref="PageVerdict"/> to
    /// learn.
    /// </summary>
    public ScraperEngineBuilder Process(Action<JsonObject> enrich)
    {
        SpiderBuilder.AddProcessor(new DelegatePageProcessor((context, _) =>
        {
            enrich(context.Data.Data);
            return ValueTask.FromResult(PageVerdict.Keep(context.Data));
        }));
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
    /// Produce just the immutable <see cref="ScraperConfig"/> (no engine, no
    /// persistence) — the gated config terminal. The distributed start
    /// endpoint uses this, then persists the result to shared config storage
    /// for workers (ADR-0009; see <c>Examples/WebReaper.AzureFuncs</c>). Only
    /// reachable after <see cref="Crawl(string[])"/> + <see cref="ICrawlSeed.Extract"/>,
    /// so start URLs and a schema are always present (ADR-0025); the
    /// reduced-shell worker that consumes the persisted config is built with
    /// <see cref="DistributedSpiderBuilder"/>.
    /// </summary>
    public ScraperConfig Build() => ConfigBuilder.Build();

    /// <summary>
    /// Build the engine: persist the <see cref="ScraperConfig"/> to the
    /// configured <see cref="IScraperConfigStorage"/>, construct the
    /// <see cref="ISpider"/>, and return a runnable <see cref="ScraperEngine"/>
    /// (the in-process Crawl driver, ADR-0022). Start URLs and a schema are
    /// guaranteed present — this is reachable only via
    /// <see cref="Crawl(string[])"/> + <see cref="ICrawlSeed.Extract"/>
    /// (ADR-0025), so it cannot fail for an unconfigured crawl.
    /// </summary>
    public async Task<ScraperEngine> BuildAsync()
    {
        // ADR-0034: build the immutable config, hand it to the SpiderBuilder
        // (the shell takes its Headless + ParsingScheme from it), then persist
        // it. The engine still reads ConfigStorage itself in RunAsync.
        var config = ConfigBuilder.Build();
        SpiderBuilder.WithConfig(config);
        var spider = SpiderBuilder.Build();
        await ConfigStorage.CreateConfigAsync(config);

        return new ScraperEngine(
            _parallelismDegree, ConfigStorage, Scheduler, spider,
            SpiderBuilder.DriverLinkTracker, SpiderBuilder.DriverSinks, Logger,
            SpiderBuilder.DriverPageProcessors,
            retryPolicy: SpiderBuilder.DriverRetryPolicy);
    }
}
