using System.Collections.Immutable;
using System.Net;
using AngleSharp;
using AngleSharp.Dom;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Linq;
using Serilog;
using System.Text;

namespace WebReaper;

// anglesharpjs
// puppeter
public class Scraper2
{
    protected List<string> linkPathSelectors = new();
    protected ImmutableHashSet<string> visited = ImmutableHashSet.Create<string>();
    protected int limit = int.MaxValue;
    private string filePath = "output.json";
    private string startUrl;
    private WebEl[]? schema;
    private string? paginationSelector;
    private WebProxy proxy;
    private WebProxy[] proxies;

    public Scraper2(string startUrl)
    {
        ServicePointManager.DefaultConnectionLimit = int.MaxValue;
        this.startUrl = startUrl;
    }

    public Scraper2 FollowLinks(string linkSelector)
    {
        linkPathSelectors.Add(linkSelector);
        return this;
    }

    public Scraper2 WithScheme(WebEl[] schema)
    {
        this.schema = schema;
        return this;
    }

    public Scraper2 Limit(int limit)
    {
        this.limit = limit;
        return this;
    }

    public Scraper2 WithProxy(WebProxy proxy)
    {
        this.proxy = proxy;
        return this;
    }

    public Scraper2 WithProxy(WebProxy[] proxies)
    {
        this.proxies = proxies;
        return this;
    }

    public Scraper2 WithPuppeter(WebProxy[] proxies)
    {
        return this;
    }

    public Scraper2 WithJinit(WebProxy[] proxies)
    {
        return this;
    }

    public Scraper2 To(string filePath)
    {
        this.filePath = filePath;
        return this;
    }

    public Scraper2 Paginate(string paginationSelector)
    {
        this.paginationSelector = paginationSelector;
        return this;
    }

    public async Task Run()
    {
        var result = await CrawlAsync(startUrl);

        Log.Logger.Information("Crawled {count} pages", visited.Count);

        Log.Logger.Information("Saving results");

        File.Delete(filePath);

        var resultBuilder = new StringBuilder();

        //var build = 

        resultBuilder.AppendJoin("," + Environment.NewLine, result.Select(r =>
        {
            var result = GetJson(r);
            if(string.IsNullOrEmpty(result["title"].ToString())) {
                Log.Logger.Warning("Shit {0}, {1},\n {2}", r.Url, r.Title, r.ToHtml());
            }
            return JsonConvert.SerializeObject(result);
        }));

        await File.AppendAllTextAsync(filePath, "[" + Environment.NewLine);
        await File.AppendAllTextAsync(filePath, resultBuilder.ToString());
        await File.AppendAllTextAsync(filePath, "]" + Environment.NewLine);

        Log.Logger.Information("Done");
    }

    private JObject GetJson(IDocument doc)
    {
        var output = new JObject();

        foreach (var item in schema)
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

    protected async Task<IDocument[]> CrawlAsync(string url)
    {
        var linksToTargetPages = await GetLinksToTargetPages(url);

        Log.Logger.Information("Started downloading {count} pages", linksToTargetPages.Count());

        var docs = await DownloadTargetPages(linksToTargetPages.Take(2000));

        return docs;
    }

    protected async Task<IEnumerable<string>> GetLinksToTargetPages(params string[] links)
    {
        IEnumerable<string> currentLinks = new List<string>(links);

        var paginatedPages = Array.Empty<IDocument>();

        for (int i = 0; i < linkPathSelectors.Count; i++)
        {
            Log.Logger.Information("Collecting links with selector {selector} on level {level}",
                linkPathSelectors[i],
                i);

            var pageTasks = currentLinks.Select(link => GetDocumentAsync(link));
            var pages = await Task.WhenAll(pageTasks);

            currentLinks = GetLinksFromPages(pages, linkPathSelectors[i])
                .ToArray();

            if (paginationSelector != null && i == linkPathSelectors.Count - 1)
            {
                paginatedPages = pages;
            }
        }

        if (paginationSelector == null)
        {
            return currentLinks;
        }

        Log.Logger.Information("Processing pagination");

        IEnumerable<string> linksToPaginatedPages = GetLinksFromPages(paginatedPages, paginationSelector);

        var targetLinks = new HashSet<string>(currentLinks);
        var visitedPaginatedPages = new HashSet<string>(paginatedPages.Select(p => p.Url));

        linksToPaginatedPages = linksToPaginatedPages.Except(visitedPaginatedPages);

        while (linksToPaginatedPages.Any())
        {
            if(targetLinks.Count >= limit) {
                break;
            }
            
            Log.Logger.Information("Downloading {count} paginated pages", linksToPaginatedPages.Count());

            var paginatedPagesTasks = linksToPaginatedPages
                .Select(link => GetDocumentAsync(link));

            paginatedPages = await Task.WhenAll(paginatedPagesTasks);

            AddTargetLinks(paginatedPages, targetLinks);

            linksToPaginatedPages =
                GetLinksFromPages(paginatedPages, paginationSelector)
                .Except(visitedPaginatedPages)
                .ToArray();

            visitedPaginatedPages.UnionWith(linksToPaginatedPages);

            GC.Collect(2);
        }

        return targetLinks;
    }

    private IEnumerable<string> GetLinksFromPages(IDocument[] paginatedPages, string selector)
    {
        return paginatedPages.Select(document =>
                        document.QuerySelectorAll(selector)
                                .Select(e => e.HyperReference(e.Attributes["href"].Value).ToString())
                                .Distinct()
                                .ToList())
                        .SelectMany(p => p);
    }

    private void AddTargetLinks(IDocument[] paginatedPages, HashSet<string> targetLinks)
    {
        var newLinks = paginatedPages.Select(document =>
                        document.QuerySelectorAll(linkPathSelectors.Last())
                                .Select(e => e.HyperReference(e.Attributes["href"].Value).ToString())
                                .Distinct()
                                .ToList())
                        .SelectMany(p => p);

        targetLinks.UnionWith(newLinks);
    }

    protected async Task<IDocument[]> DownloadTargetPages(IEnumerable<string> links)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();

        Log.Logger.Information("Downloading {count} target pages", links.Count());

        var tasks = links.Select(link => GetDocumentAsync(link));

        var result = await Task.WhenAll(tasks);

        Log.Logger.Information("Finished downloading {count} target pages", result.Count());

        watch.Stop();

        Log.Logger.Information("Method {method}. Docs count: {count}. Elapsed: {elapsed} sec",
            nameof(DownloadTargetPages),
            result.Count(),
            watch.Elapsed.TotalSeconds);

        return result;
    }

    protected async Task<IDocument> GetDocumentAsync(string url)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();

       // We require a custom configuration with JavaScript, CSS and the default loader
            var config = Configuration.Default
                                      .WithJs()
                                      .WithCss()
                                      .WithDefaultLoader();



        var context = BrowsingContext.New(config);

        var document = await context.OpenAsync(url);

        await document.WaitForReadyAsync();

        watch.Stop();

        // Log.Logger.Information("Method {method}. Elapsed: {elapsed} sec",
        //     nameof(GetDocumentAsync),
        //     watch.Elapsed.TotalSeconds);

        return document;
    }

    private async Task SaveAsync(IDocument doc)
    {
        var result = GetJson(doc);
        await File.AppendAllTextAsync(filePath, JsonConvert.SerializeObject(result) + ",");
    }
}
