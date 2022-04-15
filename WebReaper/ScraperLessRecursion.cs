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
public class ScraperLessRecursion
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

    public ScraperLessRecursion(string startUrl)
    {
        this.startUrl = startUrl;
    }

    public ScraperLessRecursion FollowLinks(string linkSelector)
    {
        linkPathSelectors.AddLast(linkSelector);
        return this;
    }

    public ScraperLessRecursion WithScheme(WebEl[] schema)
    {
        this.schema = schema;
        return this;
    }

    public ScraperLessRecursion Limit(int limit)
    {
        this.limit = limit;
        return this;
    }

    public ScraperLessRecursion WithProxy(WebProxy proxy)
    {
        this.proxy = proxy;
        return this;
    }

    public ScraperLessRecursion WithProxy(WebProxy[] proxies)
    {
        this.proxies = proxies;
        return this;
    }

    public ScraperLessRecursion WithPuppeter(WebProxy[] proxies)
    {
        return this;
    }

    public ScraperLessRecursion WithJinit(WebProxy[] proxies)
    {
        return this;
    }

    public ScraperLessRecursion To(string filePath)
    {
        this.filePath = filePath;
        return this;
    }

    public ScraperLessRecursion Paginate(string paginationSelector)
    {
        this.paginationSelector = paginationSelector;
        return this;
    }

    public async Task Run()
    {
        var result = await GetTargetPages(startUrl, linkPathSelectors.First);

        Log.Logger.Information("Crawled {count} pages", visited.Count);

        Log.Logger.Information("Getting target pages");

        var pages = await Task.WhenAll(result.Select(r => GetDocument(r)));

        Log.Logger.Information("Crawled {count} pages", visited.Count);

        Log.Logger.Information("Saving results");

        File.Delete(filePath);

        var txtResults = string.Join(",", pages.Select(r => {
            var result = GetJson(r);
            var res = JsonConvert.SerializeObject(result) + Environment.NewLine;
            return res;
        }));

        await File.AppendAllTextAsync(filePath, "[" + Environment.NewLine);
        await File.AppendAllTextAsync(filePath, txtResults);
        await File.AppendAllTextAsync(filePath, "]" + Environment.NewLine);

        Log.Logger.Information("Done");
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

    private async Task<IEnumerable<string>> GetTargetPages(string url, LinkedListNode<string> selector)
    {
        Log.Logger.Information("Visiting {url} with selector {selector}", url, selector.Value);

        if (visited.Contains(url))
        {
            Log.Logger.Warning("Already visited {url} with selector {selector}", url, selector.Value);
            return Enumerable.Empty<string>();
        }

        if (visited.Count >= limit)
        {
            Log.Logger.Warning("Reached the limit {limit}", limit);
            return Array.Empty<string>();
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
            //IDocument[] result = await DownloadTargetPages(links);

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
                var targetLinks = nextPageTargetPages.SelectMany(p => p);

                links = links.Concat(targetLinks).ToList();
            }

            return links;
        }

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
