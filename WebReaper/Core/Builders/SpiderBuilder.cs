using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using WebReaper.HttpRequests.Concrete;
using WebReaper.Parser.Concrete;
using WebReaper.LinkTracker.Concrete;
using WebReaper.Loaders.Concrete;
using WebReaper.Sinks.Concrete;
using WebReaper.Parser.Abstract;
using WebReaper.Loaders.Abstract;
using WebReaper.LinkTracker.Abstract;
using WebReaper.Sinks.Abstract;
using WebReaper.Spider.Abstract;
using WebReaper.Spider.Concrete;
using WebReaper.Proxy.Abstract;

namespace WebReaper.Core.Builders;

public class SpiderBuilder
{
    public SpiderBuilder()
    {
        // default implementations
        Logger = NullLogger.Instance;
        ContentParser = new ContentParser(Logger);
        LinkParser = new LinkParserByCssSelector();
        SiteLinkTracker = new InMemoryVisitedLinkTracker();
    }

    private List<IScraperSink> Sinks { get; } = new();

    private int limit = int.MaxValue;

    private ILogger Logger { get; set; }

    private ILinkParser LinkParser { get; }

    private IVisitedLinkTracker SiteLinkTracker { get; set; }

    private IContentParser ContentParser { get; }

    private IStaticPageLoader StaticPageLoader { get; set; }

    private IBrowserPageLoader BrowserPageLoader { get; set; }

    private IProxyProvider ProxyProvider { get; set; }

    private CookieContainer Cookies { get; } = new();

    protected event Action<JObject> ScrapedData;

    private List<string> _urlBlackList = new();

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

    public SpiderBuilder Authorize(Func<CookieContainer> authorize)
    {
        var cookieContainer = authorize();

        //httpHandler.CookieContainer = cookieContainer;
        Cookies.Add(cookieContainer.GetAllCookies());

        return this;
    }

    public SpiderBuilder AddSink(IScraperSink sink)
    {
        Sinks.Add(sink);
        return this;
    }

    public SpiderBuilder IgnoreUrls(params string[] urls)
    {
        _urlBlackList.AddRange(urls);
        return this;
    }

    public SpiderBuilder Limit(int limit)
    {
        this.limit = limit;
        return this;
    }

    public SpiderBuilder WriteToConsole() => AddSink(new ConsoleSink());

    public SpiderBuilder AddSubscription(Action<JObject> eventHandler)
    {
        ScrapedData += eventHandler;
        return this;
    }

    public SpiderBuilder WriteToJsonFile(string filePath) => AddSink(new JsonLinesFileSink(filePath));

    public SpiderBuilder WriteToCosmosDb(
        string endpointUrl,
        string authorizationKey,
        string databaseId,
        string containerId)
    {
        return AddSink(new CosmosSink(endpointUrl, authorizationKey, databaseId, containerId, Logger));
    }

    public SpiderBuilder WithStaticPageLoader(IStaticPageLoader staticPageLoader)
    {
        StaticPageLoader = staticPageLoader;
        return this;
    }

    public SpiderBuilder WithBrowserPageLoader(IBrowserPageLoader browserPageLoader)
    {
        BrowserPageLoader = browserPageLoader;
        return this;
    }

    public SpiderBuilder WithProxies(IProxyProvider proxyProvider)
    {
        ProxyProvider = proxyProvider;
        return this;
    }

    public SpiderBuilder WriteToCsvFile(string filePath) => AddSink(new CsvFileSink(filePath));

    public ISpider Build()
    {
        if (ProxyProvider != null)
        {
            BrowserPageLoader ??= new PuppeteerPageLoaderWithProxies(Logger, ProxyProvider, Cookies);

            var req = new RotatingProxyRequests(ProxyProvider)
            {
                CookieContainer = Cookies
            };

            StaticPageLoader ??= new HttpStaticPageLoader(req, Logger);
        }
        else
        {
            var req = new Requests
            {
                CookieContainer = Cookies
            };
            StaticPageLoader ??= new HttpStaticPageLoader(req, Logger);
            BrowserPageLoader ??= new PuppeteerPageLoader(Logger, Cookies);
        }

        var spider = new WebReaperSpider(
            Sinks,
            LinkParser,
            ContentParser,
            SiteLinkTracker,
            StaticPageLoader,
            BrowserPageLoader,
            Logger)
        {
            UrlBlackList = _urlBlackList.ToList(),
            PageCrawlLimit = limit
        };

        spider.ScrapedData += ScrapedData;

        return spider;
    }
}
