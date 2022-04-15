using System.Collections.Immutable;
using System.Net;
using AngleSharp;
using AngleSharp.Dom;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Linq;
using Serilog;

namespace WebReaper;

// anglesharpjs
// puppeter
public class Scraper
{
    protected LinkedList<string> linkPathSelectors = new();
    protected ImmutableHashSet<string> visited = ImmutableHashSet.Create<string>();
    protected int limit = int.MaxValue;
    private string filePath = "output.json";
    private string startUrl;
    private WebEl[]? schema;
    private string? paginationSelector;
    private WebProxy proxy;
    private WebProxy[] proxies;

    public Scraper(string startUrl)
    {
        this.startUrl = startUrl;
    }

    public Scraper FollowLinks(string linkSelector)
    {
        linkPathSelectors.AddLast(linkSelector);
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

    public Scraper WithJinit(WebProxy[] proxies)
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

    public async Task Run()
    {
        var result = await GetTargetPages(startUrl, linkPathSelectors.First);

        Log.Logger.Information("Crawled {count} pages", visited.Count);

        Log.Logger.Information("Saving results");

        File.Delete(filePath);

        var txtResults = string.Join(",", result.Select(r => {
            var result = GetJson(r);
            var res = JsonConvert.SerializeObject(result);
            return res;
        }));

        await File.AppendAllTextAsync(filePath, "[" + Environment.NewLine);
        await File.AppendAllTextAsync(filePath, txtResults);
        await File.AppendAllTextAsync(filePath, "]" + Environment.NewLine);

        Log.Logger.Information("Finished");
    }

    private JObject GetJson(IDocument doc) {
        var output = new JObject();

        foreach(var item in schema) 
        {
            var result = FillOutput(output, doc, item);
        }

        return output;
    }

    private JObject FillOutput(JObject obj, IDocument doc, WebEl item)
    {
        switch (item.Type)
        {
            case JsonType.String: 
                obj[item.Field] = doc.QuerySelector(item.Selector)?.TextContent;
                break;
            case JsonType.Number: 
                obj[item.Field] = Double.Parse(doc.QuerySelector(item.Selector).TextContent);
                break;
            case JsonType.Boolean: 
                obj[item.Field] = bool.Parse(doc.QuerySelector(item.Selector).TextContent);
                break;
            case JsonType.Image: 
                obj[item.Field] = doc.QuerySelector(item.Selector)?.GetAttribute("title");
                break;
            case JsonType.Html: 
                obj[item.Field] = doc.QuerySelector(item.Selector).Html();
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

    private async Task<IDocument[]> GetTargetPages(string url, LinkedListNode<string> selector)
    {
        Log.Logger.Information("Visiting {url} with selector {selector}", url, selector.Value);

        if (visited.Contains(url))
        {
            Log.Logger.Warning("Already visited {url} with selector {selector}", url, selector.Value);
            return Array.Empty<IDocument>();
        }

        if (visited.Count >= limit)
        {
            Log.Logger.Warning("Reached the limit {}", limit);
            return Array.Empty<IDocument>();
        }

        ImmutableInterlocked.Update(ref visited, old => old.Add(url));
        Log.Logger.Information("Visited {count} links", visited.Count);

        IDocument document = await GetDocument(url);

        var links = document
            .QuerySelectorAll(selector.Value)
            .Select(e => e.HyperReference(e.Attributes["href"].Value).ToString())
            .Distinct()
            .ToList();

        if (selector.Next == null)
        {
            Log.Logger.Information("Reached page with target links {url}", url);
            IDocument[] result = await DownloadTargetPages(links);

            if (paginationSelector != null)
            {
                var nextPageLinks = document
                    .QuerySelectorAll(paginationSelector)
                    .Select(e => e.HyperReference(e.Attributes["href"].Value).ToString())
                    .Distinct();

                var notVisitedLinks = nextPageLinks.Where(l => !visited.Contains(l));

                var nextPageTargetPagesTasks = notVisitedLinks
                    .Select(link => GetTargetPages(link, selector));

                var nextPageTargetPages = await Task.WhenAll(nextPageTargetPagesTasks);

                result = result.Concat(nextPageTargetPages.SelectMany(p => p)).ToArray();
            }

            return result;
        }

        var docs = new List<IDocument>();

        var jobs = links.Select(link => GetTargetPages(link, selector.Next));

        var taskResults = await Task.WhenAll(jobs);

        var outputResult = taskResults.SelectMany(r => r);

        return outputResult.ToArray();
    }

    protected async Task<IDocument[]> DownloadTargetPages(List<string> links)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();

        Log.Logger.Information("Downloading {count} target pages", links.Count);

        var notVisitedLinks = links.Where(link => !visited.Contains(link));

        var tasks = notVisitedLinks.Select(link => GetDocument(link));

        var result = await Task.WhenAll(tasks);
        Log.Logger.Information("Finished downloading {count} target pages", result.Count());

        ImmutableInterlocked.Update(ref visited, old => old.Union(notVisitedLinks));

        watch.Stop();

        Log.Logger.Information("Method {method}. Docs count: {count}. Elapsed: {elapsed} sec",
            nameof(DownloadTargetPages),
            result.Count(),
            watch.Elapsed.TotalSeconds);

        return result;
    }

    protected async Task<IDocument> GetDocument(string url)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();

        var config = Configuration.Default.WithDefaultLoader();
        var context = BrowsingContext.New(config);

        var document = await context.OpenAsync(url);
        
        watch.Stop();

        Log.Logger.Information("Method {method}. Elapsed: {elapsed} sec",
            nameof(GetDocument),
            watch.Elapsed.TotalSeconds);

        return document;
    }

    private async Task Save(IDocument doc)
    {
        var result = GetJson(doc);
        await File.AppendAllTextAsync(filePath, JsonConvert.SerializeObject(result) + ",");
    }
}
