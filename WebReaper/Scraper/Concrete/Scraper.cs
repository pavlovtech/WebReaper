using System.Net;
using Newtonsoft.Json.Linq;
using HtmlAgilityPack;
using Fizzler.Systems.HtmlAgilityPack;
using WebReaper.Domain;
using Microsoft.Extensions.Logging;
using WebReaper.Queue.Abstract;
using WebReaper.Scraper.Abstract;
using WebReaper.Queue.Concrete;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using WebReaper.Parser.Concrete;

namespace WebReaper.Scraper.Concrete;

// anglesharpjs
// puppeter
public class Scraper : IScraper
{
    protected List<LinkPathSelector> linkPathSelectors = new();
    protected int limit = int.MaxValue;

    protected BlockingCollection<Job> jobs = new(new ProducerConsumerPriorityQueue());

    private string filePath = "output.json";
    private string? startUrl;

    private SchemaElement[]? schema = Array.Empty<SchemaElement>();

    private WebProxy? proxy;

    private WebProxy[] proxies = Array.Empty<WebProxy>();

    private int parallelismDegree = 1;

    protected string baseUrl = "";

    protected readonly IJobQueueReader JobQueueReader;

    protected readonly IJobQueueWriter JobQueueWriter;

    protected ILogger Logger;

    protected ILinkParser LinkParser = new LinkParserByCssSelector();

    protected string[] urlBlackList;

    protected int ParallelismDegree { get; private set; }

    public Scraper(ILogger logger)
    {
        Logger = logger;

        JobQueueReader = new JobQueueReader(jobs);
        JobQueueWriter = new JobQueueWriter(jobs);
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

    public IScraper FollowLinks(
        string linkSelector,
        SelectorType selectorType = SelectorType.Css)
    {
        linkPathSelectors.Add(new(linkSelector));
        return this;
    }

    public IScraper Paginate(string paginationSelector)
    {
        linkPathSelectors[^1] =
            linkPathSelectors.Last() with
            {
                PaginationSelector = paginationSelector,
            };

        return this;
    }

    public IScraper IgnoreUrls(params string[] urls)
    {
        this.urlBlackList = urls;
        return this;
    }

    public IScraper WithScheme(SchemaElement[] schema)
    {
        this.schema = schema;
        return this;
    }

    public IScraper WithParallelismDegree(int parallelismDegree)
    {
        this.ParallelismDegree = parallelismDegree;
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

    public async Task Run()
    {
        ArgumentNullException.ThrowIfNull(startUrl);
        ArgumentNullException.ThrowIfNull(baseUrl);

        JobQueueWriter.Write(new Job(
            baseUrl,
            startUrl,
            ImmutableQueue.Create<LinkPathSelector>(linkPathSelectors.ToArray()),
            DepthLevel: 0));

        var spider = new WebReaper.Spider.Concrete.Spider(LinkParser, JobQueueReader, JobQueueWriter, Logger)
            .IgnoreUrls(this.urlBlackList);

        var spiderTasks = Enumerable
            .Range(0, parallelismDegree)
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

    private JObject FillOutput(JObject obj, HtmlDocument doc, SchemaElement item)
    {
        switch (item.Type)
        {
            case DataType.String:
                obj[item.Field] = doc.DocumentNode.QuerySelector(item.Selector).InnerText;
                break;
            case DataType.Number:
                obj[item.Field] = double.Parse(doc.DocumentNode.QuerySelector(item.Selector).InnerText);
                break;
            case DataType.Boolean:
                obj[item.Field] = bool.Parse(doc.DocumentNode.QuerySelector(item.Selector).InnerText);
                break;
            case DataType.Image:
                obj[item.Field] = doc.DocumentNode.QuerySelector(item.Selector).GetAttributeValue("src", "");
                break;
            case DataType.Html:
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
}
