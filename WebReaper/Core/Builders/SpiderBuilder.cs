using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WebReaper.CookieStorage.Abstract;
using WebReaper.CookieStorage.Concrete;
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
using WebReaper.Sinks.Models;

namespace WebReaper.Core.Builders;

public class SpiderBuilder
{
    private List<IScraperSink> Sinks { get; } = new();

    private int limit = int.MaxValue;

    private ILogger Logger { get; set; } = NullLogger.Instance;

    private ILinkParser LinkParser { get; set; } = new LinkParserByCssSelector();

    private IVisitedLinkTracker SiteLinkTracker { get; set; } = new InMemoryVisitedLinkTracker();

    private IContentParser? ContentParser { get; set; }

    private IStaticPageLoader? StaticPageLoader { get; set; }

    private IBrowserPageLoader? BrowserPageLoader { get; set; }

    private IProxyProvider? ProxyProvider { get; set; }

    private CookieContainer Cookies { get; set; } = new();

    private ICookiesStorage CookieStorage { get; set; } = new InMemoryCookieStorage();

    protected event Action<ParsedData> ScrapedData;

    private readonly List<string> _urlBlackList = new();

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

    public SpiderBuilder AddSubscription(Action<ParsedData> eventHandler)
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
        return AddSink(new CosmosSink(endpointUrl, authorizationKey, databaseId, containerId, Logger)); // possible NullLogger here
    }
    
    public SpiderBuilder WriteToRedis(string connectionString, string redisKey)
    {
        return AddSink(new RedisSink(connectionString, redisKey, Logger)); // possible NullLogger here
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
    
    public SpiderBuilder WithRedisCookieStorage(string connectionString, string redisKey)
    {
        CookieStorage = new RedisCookieStorage(connectionString, redisKey, Logger);
        return this;
    }

    public ISpider Build()
    {
        // default implementations
        ContentParser ??= new ContentParser(Logger);

        if (ProxyProvider != null)
        {
            BrowserPageLoader ??= new PuppeteerPageLoaderWithProxies(Logger, ProxyProvider, CookieStorage);

            var pageRequester = new RotatingProxyPageRequester(ProxyProvider);

            StaticPageLoader ??= new HttpStaticPageLoader(pageRequester, CookieStorage, Logger);
        }
        else
        {
            var pageRequester = new PageRequester();
            
            StaticPageLoader ??= new HttpStaticPageLoader(pageRequester, CookieStorage, Logger);
            BrowserPageLoader ??= new PuppeteerPageLoader(Logger, CookieStorage);
        }

        CookieStorage.AddAsync(Cookies, TimeSpan.FromHours(4));

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
