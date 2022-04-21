using System.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text;
using HtmlAgilityPack;
using Fizzler.Systems.HtmlAgilityPack;
using System.Net.Security;
using WebReaper.Domain;
using WebReaper.Queue;
using Microsoft.Extensions.Logging;

namespace WebReaper;

// anglesharpjs
// puppeter
public class Scraper
{
    protected List<string> linkPathSelectors = new();
    protected int limit = int.MaxValue;
    private string filePath = "output.json";
    private string startUrl;
    private WebEl[]? schema;
    private string? paginationSelector;
    private WebProxy proxy;
    private WebProxy[] proxies;

    protected string baseUrl;

    protected HttpClient httpClient = new(new SocketsHttpHandler()
    {
        MaxConnectionsPerServer = 100,
        SslOptions = new SslClientAuthenticationOptions
        {
            // Leave certs unvalidated for debugging
            RemoteCertificateValidationCallback = delegate { return true; },
        },
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(10),
        PooledConnectionLifetime = Timeout.InfiniteTimeSpan
    })
    {
        Timeout = TimeSpan.FromMinutes(10)
    };

    private readonly ILogger _logger;

    public Scraper(string startUrl, ILogger logger)
    {
        _logger = logger;
        ServicePointManager.DefaultConnectionLimit = int.MaxValue;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        ServicePointManager.ServerCertificateValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;

        this.startUrl = startUrl;

        var startUri = new Uri(startUrl);

        var baseUrl = startUri.GetLeftPart(System.UriPartial.Authority);
        var segments = startUri.Segments;

        this.baseUrl = baseUrl + string.Join(string.Empty, segments.SkipLast(1));
    }

    public Scraper FollowLinks(string linkSelector)
    {
        linkPathSelectors.Add(linkSelector);
        return this;
    }

    public Scraper WithScheme(WebEl[] schema)
    {
        this.schema = schema;
        return this;
    }

    public Scraper Limit(int limit)
    {
        this.limit = limit;
        return this;
    }

    public Scraper WithProxy(WebProxy proxy)
    {
        this.proxy = proxy;
        return this;
    }

    public Scraper WithProxy(WebProxy[] proxies)
    {
        this.proxies = proxies;
        return this;
    }

    public Scraper WithPuppeter(WebProxy[] proxies)
    {
        return this;
    }

    public Scraper To(string filePath)
    {
        this.filePath = filePath;
        return this;
    }

    public Scraper Paginate(string paginationSelector)
    {
        this.paginationSelector = paginationSelector;
        return this;
    }

    IJobQueue jobQueue = new JobQueue();

    public async Task Run()
    {
        jobQueue.Add(new Job(baseUrl,
            startUrl,
            linkPathSelectors.ToArray(),
            paginationSelector,
            DepthLevel: 0,
            Priority: 0));

        var spider = new Spider.Spider(jobQueue, _logger);
        var s1 = spider.Crawl();
        var s2 = spider.Crawl();
        var s3 = spider.Crawl();
        var s4 = spider.Crawl();
        var s5 = spider.Crawl();
        var s6 = spider.Crawl();
        var s7 = spider.Crawl();
        var s8 = spider.Crawl();

        await Task.WhenAll(s1,s2,s3,s4,s5,s6, s7, s8);
    }

    private JObject GetJson(HtmlDocument doc)
    {
        var output = new JObject();

        foreach (var item in schema)
        {
            var result = FillOutput(output, doc, item);
        }

        return output;
    }

    private JObject FillOutput(JObject obj, HtmlDocument doc, WebEl item)
    {
        switch (item.Type)
        {
            case JsonType.String:
                obj[item.Field] = doc.DocumentNode.QuerySelector(item.Selector).InnerText;
                break;
            case JsonType.Number:
                obj[item.Field] = Double.Parse(doc.DocumentNode.QuerySelector(item.Selector).InnerText);
                break;
            case JsonType.Boolean:
                obj[item.Field] = bool.Parse(doc.DocumentNode.QuerySelector(item.Selector).InnerText);
                break;
            case JsonType.Image:
                obj[item.Field] = doc.DocumentNode.QuerySelector(item.Selector).GetAttributeValue("src", "");
                break;
            case JsonType.Html:
                obj[item.Field] = doc.DocumentNode.QuerySelector(item.Selector).InnerHtml;
                break;
                // case JsonType.Array: 
                //     var arr = new JArray();
                //     obj[item.Field] = arr;
                //     foreach(var el in item.Children) {
                //         var result = FillOutput(doc, el);
                //         arr.Add(result);
                //     }
                //     break;
        }

        return obj;
    }

    protected async Task<HtmlDocument> GetDocumentAsync(string url)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var htmlDoc = new HtmlDocument();
        htmlDoc.Load(await httpClient.GetStreamAsync(url), System.Text.Encoding.GetEncoding(1251));
        watch.Stop();
        // Log.Logger.Information("Method {method}. Elapsed: {elapsed} sec",
        //     nameof(GetDocumentAsync),
        //     watch.Elapsed.TotalSeconds);

        return htmlDoc;
    }
}
