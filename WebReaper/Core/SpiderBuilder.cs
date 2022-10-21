using System.Net;
using Microsoft.Extensions.Logging;
using System.Net.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
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

namespace WebReaper.Core;

public class SpiderBuilder
{
    public SpiderBuilder()
    {
        // default implementations
        Logger = NullLogger.Instance;
        ContentParser = new ContentParser(Logger);
        LinkParser = new LinkParserByCssSelector();
        SiteLinkTracker = new InMemoryCrawledLinkTracker();
    }

    public List<IScraperSink> Sinks { get; } = new();

    protected int limit = int.MaxValue;

    protected ILogger Logger { get; set; }

    protected ILinkParser LinkParser { get; set; }

    protected ICrawledLinkTracker SiteLinkTracker { get; set; }

    protected IContentParser ContentParser { get; }

    protected IStaticPageLoader StaticPageLoader { get; set; }
    protected IDynamicPageLoader DynamicPageLoader { get; set; }

    protected CookieContainer Cookies { get; } = new();

    protected event Action<JObject> ScrapedData;

    protected List<string> UrlBlackList = new();

    public SpiderBuilder WithLogger(ILogger logger)
    {
        Logger = logger;
        return this;
    }

    public SpiderBuilder WithLinkTracker(ICrawledLinkTracker linkTracker)
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
        UrlBlackList.AddRange(urls);
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

    public SpiderBuilder WithBrowserPageLoader(IDynamicPageLoader dynamicPageLoader)
    {
        DynamicPageLoader = dynamicPageLoader;
        return this;
    }

    public SpiderBuilder WithProxies(IProxyProvider proxyProvider)
    {
        ProxyProvider = proxyProvider;
        return this;
    }

    public SpiderBuilder WriteToCsvFile(string filePath) => AddSink(new CsvFileSink(filePath));

    protected IProxyProvider ProxyProvider { get; set; }

    public ISpider Build()
    {
        IHttpRequests req = new Requests();

        if (ProxyProvider != null)
        {
            req = new RotatingProxyRequests(ProxyProvider);
        }

        req.CookieContainer = Cookies;

        StaticPageLoader ??= new HttpStaticPageLoader(req, Logger);

        DynamicPageLoader ??= new PuppeteerPageLoader(Logger, Cookies, ProxyProvider);

        ISpider spider = new WebReaperSpider(
            Sinks,
            LinkParser,
            ContentParser,
            SiteLinkTracker,
            StaticPageLoader,
            DynamicPageLoader,
            Logger)
        {
            UrlBlackList = UrlBlackList.ToList(),
            PageCrawlLimit = limit
        };

        spider.ScrapedData += ScrapedData;

        return spider;
    }
}
