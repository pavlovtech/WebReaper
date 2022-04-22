using System.Net;
using Newtonsoft.Json.Linq;
using System.Text;
using HtmlAgilityPack;
using Fizzler.Systems.HtmlAgilityPack;
using System.Net.Security;
using WebReaper.Domain;
using Microsoft.Extensions.Logging;
using WebReaper.Queue.Abstract;
using WebReaper.Scraper.Abstract;
using WebReaper.Queue.Concrete;
using System.Collections.Concurrent;

namespace WebReaper.Scraper.Concrete;

// anglesharpjs
// puppeter
public class Scraper : IScraper
{
    protected List<string> linkPathSelectors = new();
    protected int limit = int.MaxValue;

    protected BlockingCollection<Job> jobs = new(new ProducerConsumerPriorityQueue());

    private string filePath = "output.json";
    private string startUrl;

    private WebEl[]? schema;

    private string? paginationSelector;

    private WebProxy proxy;

    private WebProxy[] proxies;

    private int spidersCount = 1;

    protected string baseUrl;

    protected HttpMessageHandler HttpHandler;

    protected HttpClient HttpClient;

    protected readonly IJobQueueReader JobQueueReader;

    protected readonly IJobQueueWriter JobQueueWriter;

    protected ILogger Logger;

    public Scraper(ILogger logger)
    {
        InitHttpClient();

        Logger = logger;

        ServicePointManager.DefaultConnectionLimit = int.MaxValue;

        JobQueueReader = new JobQueueReader(jobs);
        JobQueueWriter = new JobQueueWriter(jobs);

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        ServicePointManager.ServerCertificateValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;
    }

    private void InitHttpClient()
    {
        this.HttpHandler = new SocketsHttpHandler()
        {
            MaxConnectionsPerServer = 100,
            SslOptions = new SslClientAuthenticationOptions
            {
                // Leave certs unvalidated for debugging
                RemoteCertificateValidationCallback = delegate { return true; },
            },
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(10),
            PooledConnectionLifetime = Timeout.InfiniteTimeSpan
        };

        this.HttpClient = new HttpClient(HttpHandler);
    }

    public IScraper WithStartUrl(string startUrl)
    {
        this.startUrl = startUrl;

        var startUri = new Uri(startUrl);

        var baseUrl = startUri.GetLeftPart(UriPartial.Authority);
        var segments = startUri.Segments;

        this.baseUrl = baseUrl + string.Join(string.Empty, segments.SkipLast(1));

        return this;
    }

    public IScraper FollowLinks(string linkSelector)
    {
        linkPathSelectors.Add(linkSelector);
        return this;
    }

    public IScraper WithScheme(WebEl[] schema)
    {
        this.schema = schema;
        return this;
    }

    public IScraper WithSpiders(int spiderCount)
    {
        spidersCount = spiderCount;
        return this;
    }

    public IScraper Limit(int limit)
    {
        this.limit = limit;
        return this;
    }

    public IScraper WithProxy(WebProxy proxy)
    {
        this.proxy = proxy;
        return this;
    }

    public IScraper WithProxy(WebProxy[] proxies)
    {
        this.proxies = proxies;
        return this;
    }

    public IScraper WithPuppeter(WebProxy[] proxies)
    {
        return this;
    }

    public IScraper To(string filePath)
    {
        this.filePath = filePath;
        return this;
    }

    public IScraper Paginate(string paginationSelector)
    {
        this.paginationSelector = paginationSelector;
        return this;
    }

    public async Task Run()
    {
        jobQueueWriter.Write(new Job(baseUrl,
            startUrl,
            linkPathSelectors.ToArray(),
            paginationSelector,
            DepthLevel: 0,
            Priority: 0));

        var spider = new WebReaper.Spider.Concrete.Spider(jobQueueReader, jobQueueWriter, _logger);

        var spiderTasks = Enumerable
            .Range(0, spidersCount)
            .Select(_ => spider.Crawl());

        await Task.WhenAll(spiderTasks);
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
                obj[item.Field] = double.Parse(doc.DocumentNode.QuerySelector(item.Selector).InnerText);
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
        htmlDoc.LoadHtml(await HttpClient.GetStringAsync(url));
        watch.Stop();
        // Log.Logger.Information("Method {method}. Elapsed: {elapsed} sec",
        //     nameof(GetDocumentAsync),
        //     watch.Elapsed.TotalSeconds);

        return htmlDoc;
    }
}
