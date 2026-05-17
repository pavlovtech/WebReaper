using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json.Nodes;
using WebReaper.ConfigStorage.Abstract;
using WebReaper.ConfigStorage.Concrete;
using WebReaper.Core.CookieStorage.Abstract;
using WebReaper.Core.CookieStorage.Concrete;
using WebReaper.Core.LinkTracker.Abstract;
using WebReaper.Core.LinkTracker.Concrete;
using WebReaper.Core.Loaders.Abstract;
using WebReaper.Core.Loaders.Concrete;
using WebReaper.Core.Parser.Abstract;
using WebReaper.Core.Parser.Concrete;
using WebReaper.Core.Crawling.Concrete;
using WebReaper.Core.Spider.Abstract;
using WebReaper.Core.Spider.Concrete;
using WebReaper.Domain;
using WebReaper.Proxy;
using WebReaper.Proxy.Abstract;
using WebReaper.Proxy.Concrete;
using WebReaper.Sinks.Abstract;
using WebReaper.Sinks.Concrete;
using WebReaper.Sinks.Models;

namespace WebReaper.Builders;

/// <summary>
/// Internal collaborator of <see cref="ScraperEngineBuilder"/> (ADR-0009):
/// the runtime-component wiring (loaders, parsers, sinks, trackers, cookie
/// storage). Not public — the registration seam lives only on
/// <see cref="ScraperEngineBuilder"/>; a bare <see cref="Core.Spider.Abstract.ISpider"/>
/// is obtained via <see cref="ScraperEngineBuilder.BuildSpider"/>.
/// </summary>
internal class SpiderBuilder
{
    private Func<Metadata, JsonObject, Task> PostProcessor { get; set; }

    private List<IScraperSink> Sinks { get; } = new();

    private ILogger Logger { get; set; } = NullLogger.Instance;

    private ILinkParser LinkParser { get; } = new LinkParserByCssSelector();

    private IScraperConfigStorage ScraperConfigStorage { get; set; }

    private IVisitedLinkTracker SiteLinkTracker { get; set; } = new InMemoryVisitedLinkTracker();

    private IJsonContentParser? ContentParser { get; set; }

    private IPageLoader? PageLoader { get; set; }

    private Func<ICookiesStorage, IProxyProvider?, ILogger, IPageLoadTransport>? DynamicPageLoadTransportFactory { get; set; }

    private IProxyProvider? ProxyProvider { get; set; }

    private CookieContainer Cookies { get; } = new();

    private ICookiesStorage CookieStorage { get; set; } = new InMemoryCookieStorage();

    protected event Action<ParsedData> ScrapedData;

    public SpiderBuilder WithContentParser(IJsonContentParser contentParser)
    {
        ContentParser = contentParser;
        return this;
    }

    /// <summary>
    /// Parse responses as JSON instead of HTML (issue #27). Schema
    /// selectors become JSONPath expressions. For a different document
    /// shape (e.g. HtmlAgilityPack, XPath), implement
    /// <see cref="Core.Parser.Abstract.ISchemaBackend{TNode}"/> and pass
    /// <c>new SchemaContentParser&lt;TNode&gt;(backend, logger)</c> to
    /// <see cref="WithContentParser"/> — it reuses the shared Schema fold
    /// rather than re-implementing the walk.
    /// </summary>
    public SpiderBuilder WithJsonContentParser()
    {
        ContentParser = new JsonContentParser(Logger);
        return this;
    }

    /// <summary>
    /// Parse responses as HTML but with XPath 1.0 selectors instead of CSS
    /// (discussion #17). Schema selectors become XPath expressions over the
    /// same AngleSharp DOM the default CSS parser uses; <c>IsList</c>,
    /// attributes and type coercion behave identically. Like
    /// <see cref="WithJsonContentParser"/> this is just a different
    /// <see cref="Core.Parser.Abstract.ISchemaBackend{TNode}"/> behind the
    /// one shared Schema fold (ADR 0002 / 0007).
    /// </summary>
    public SpiderBuilder WithXPathContentParser()
    {
        ContentParser = new XPathContentParser(Logger);
        return this;
    }

    public SpiderBuilder WithLogger(ILogger logger)
    {
        Logger = logger;
        return this;
    }

    public SpiderBuilder WithLinkTracker(IVisitedLinkTracker linkTracker)
    {
        SiteLinkTracker = linkTracker;
        return this;
    }

    public SpiderBuilder WithConfigStorage(IScraperConfigStorage scraperConfigStorage)
    {
        ScraperConfigStorage = scraperConfigStorage;
        return this;
    }

    public SpiderBuilder WithFileConfigStorage(string fileName)
    {
        ScraperConfigStorage = new FileScraperConfigStorage(fileName);
        return this;
    }

    public SpiderBuilder SetCookies(Action<CookieContainer> setCookies)
    {
        setCookies(Cookies);
        return this;
    }

    public SpiderBuilder AddSink(IScraperSink sink)
    {
        Sinks.Add(sink);
        return this;
    }

    public SpiderBuilder WriteToConsole()
    {
        return AddSink(new ConsoleSink());
    }

    public SpiderBuilder AddSubscription(Action<ParsedData> eventHandler)
    {
        ScrapedData += eventHandler;
        return this;
    }

    public SpiderBuilder WriteToJsonFile(string filePath, bool dataCleanupOnStart)
    {
        return AddSink(new JsonLinesFileSink(filePath, dataCleanupOnStart));
    }

    /// <summary>
    /// Supply a custom page loader. By default <see cref="Build"/> wires the
    /// HTTP and headless-browser transports behind one <see cref="PageLoader"/>
    /// and passes the optional proxy provider to both (ADR 0004).
    /// </summary>
    public SpiderBuilder WithPageLoader(IPageLoader pageLoader)
    {
        PageLoader = pageLoader;
        return this;
    }

    /// <summary>
    /// Register the transport used for Dynamic (headless-browser) pages
    /// (ADR-0009). The factory is invoked at <see cref="Build"/> time with
    /// the builder's resolved cookie storage, optional proxy provider and
    /// logger — the same collaborators the HTTP transport gets — so a
    /// satellite (e.g. WebReaper.Puppeteer's <c>.WithPuppeteerPageLoader()</c>)
    /// needs no arguments and the pre-7.0 default behaviour (one shared
    /// cookie container, issue #26) is preserved. With no registration the
    /// core default is HTTP-only and a Dynamic load throws an actionable
    /// message (<see cref="BrowserNotConfiguredPageLoadTransport"/>).
    /// </summary>
    public SpiderBuilder WithLoadTransport(
        Func<ICookiesStorage, IProxyProvider?, ILogger, IPageLoadTransport> dynamicTransportFactory)
    {
        DynamicPageLoadTransportFactory = dynamicTransportFactory;
        return this;
    }

    public SpiderBuilder WithProxies(IProxyProvider proxyProvider)
    {
        ProxyProvider = proxyProvider;
        return this;
    }

    /// <summary>
    /// Use proxies from <paramref name="source"/>, but only after they
    /// pass every supplied validator. Plugs into the same pipeline as
    /// <see cref="WithProxies(IProxyProvider)"/>.
    /// </summary>
    public SpiderBuilder WithValidatedProxies(
        IProxySource source,
        IEnumerable<IProxyValidator> validators,
        ValidatedProxyProviderOptions? options = null)
    {
        ProxyProvider = new ValidatedProxyProvider(source, validators, options, Logger);
        return this;
    }

    public SpiderBuilder WithValidatedProxies(IProxySource source, params IProxyValidator[] validators)
        => WithValidatedProxies(source, (IEnumerable<IProxyValidator>)validators);

    public SpiderBuilder WriteToCsvFile(string filePath, bool dataCleanupOnStart)
    {
        return AddSink(new CsvFileSink(filePath, dataCleanupOnStart));
    }

    public SpiderBuilder WithFileCookieStorage(string fileName)
    {
        CookieStorage = new FileCookieStorage(fileName, Logger);
        return this;
    }

    public SpiderBuilder WithCookieStorage(ICookiesStorage cookiesStorage)
    {
        CookieStorage = cookiesStorage;
        return this;
    }

    public void PostProcess(Func<Metadata, JsonObject, Task> callback)
    {
        PostProcessor = callback;
    }

    public ISpider Build()
    {
        // default implementations
        ContentParser ??= new AngleSharpContentParser(Logger);

        // One loader (ADR 0004). The HTTP transport is the core default; the
        // Dynamic slot is the registered transport factory (e.g.
        // WebReaper.Puppeteer's .WithPuppeteerPageLoader()) or, with none, an
        // actionable throw — core is HTTP-only by default (ADR-0009). The
        // proxy/no-proxy choice is still a (possibly null) provider handed in
        // to whichever transports exist, not a branch.
        PageLoader ??= new PageLoader(
            new HttpPageLoadTransport(CookieStorage, ProxyProvider, Logger),
            DynamicPageLoadTransportFactory is null
                ? new BrowserNotConfiguredPageLoadTransport()
                : DynamicPageLoadTransportFactory(CookieStorage, ProxyProvider, Logger),
            Logger);

        CookieStorage.AddAsync(Cookies);

        var crawlStep = new CrawlStep(LinkParser, ContentParser);

        var spider = new Spider(
            Sinks,
            crawlStep,
            SiteLinkTracker,
            PageLoader,
            ScraperConfigStorage,
            Logger);

        spider.ScrapedData += ScrapedData;
        spider.PostProcessor += PostProcessor;

        return spider;
    }
}
