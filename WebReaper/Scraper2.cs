using System.Collections.Immutable;
using System.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Linq;
using Serilog;
using System.Text;
using RestSharp;
using HtmlAgilityPack;
using Fizzler.Systems.HtmlAgilityPack;
using System.Net.Security;

namespace WebReaper;

// anglesharpjs
// puppeter
public class Scraper2
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

    public Scraper2(string startUrl)
    {
        ServicePointManager.DefaultConnectionLimit = int.MaxValue;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        
        ServicePointManager.ServerCertificateValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;

        this.startUrl = startUrl;

        var startUri = new Uri(startUrl);

        var baseUrl = startUri.GetLeftPart(System.UriPartial.Authority);
        var segments = startUri.Segments;

        this.baseUrl = baseUrl + string.Join(string.Empty, segments.SkipLast(1));
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

        Log.Logger.Information("Got {count} pages", result.Count());

        Log.Logger.Information("Saving results");

        File.Delete(filePath);

        var resultBuilder = new StringBuilder();

        //var build = 

        resultBuilder.AppendJoin("," + Environment.NewLine, result.Select(r =>
        {
            var result = GetJson(r);
            if (string.IsNullOrEmpty(result["title"].ToString()))
            {
                Log.Logger.Error("Shit {0}, {1},\n {2}", r.DocumentNode.QuerySelector("title").InnerText, r.DocumentNode.InnerHtml);
                return r.DocumentNode.InnerHtml;
            }
            return JsonConvert.SerializeObject(result);
        }));

        await File.AppendAllTextAsync(filePath, "[" + Environment.NewLine);
        await File.AppendAllTextAsync(filePath, resultBuilder.ToString());
        await File.AppendAllTextAsync(filePath, "]" + Environment.NewLine);

        Log.Logger.Information("Done");
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

    protected async Task<HtmlDocument[]> CrawlAsync(string url)
    {
        var linksToTargetPages = await GetLinksToTargetPages(url);

        Log.Logger.Information("Started downloading {count} pages", linksToTargetPages.Count());

        var docs = await DownloadTargetPages(linksToTargetPages);

        return docs;
    }

    protected async Task<IEnumerable<string>> GetLinksToTargetPages(params string[] links)
    {
        IEnumerable<string> currentLinks = new List<string>(links);

        var paginatedPages = Array.Empty<HtmlDocument>();
        var visitedPaginatedPages = new HashSet<string>();

        for (int i = 0; i < linkPathSelectors.Count; i++)
        {
            Log.Logger.Information("Collecting links with selector {selector} on level {level}",
                linkPathSelectors[i],
                i);

            var pageTasks = currentLinks.Select(link => GetDocumentAsync(link));
            var pages = await Task.WhenAll(pageTasks);

            if (paginationSelector != null && i == linkPathSelectors.Count - 1)
            {
                visitedPaginatedPages.UnionWith(currentLinks);
                paginatedPages = pages;
            }

            currentLinks = GetLinksFromPages(pages, linkPathSelectors[i])
                .ToArray();
        }

        if (paginationSelector == null)
        {
            return currentLinks;
        }

        Log.Logger.Information("Processing pagination");

        IEnumerable<string> linksToPaginatedPages = GetLinksFromPages(paginatedPages, paginationSelector);

        var targetLinks = new HashSet<string>(currentLinks);

        linksToPaginatedPages = linksToPaginatedPages.Except(visitedPaginatedPages);

        while (linksToPaginatedPages.Any())
        {
            if (targetLinks.Count >= limit)
            {
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

    private IEnumerable<string> GetLinksFromPages(HtmlDocument[] paginatedPages, string selector)
    {
        return paginatedPages.Select(document =>
                        document.DocumentNode.QuerySelectorAll(selector)
                                .Select(e => baseUrl + e.GetAttributeValue("href", null))
                                .Distinct()
                                .ToList())
                        .SelectMany(p => p);
    }

    private void AddTargetLinks(HtmlDocument[] paginatedPages, HashSet<string> targetLinks)
    {
        var newLinks = paginatedPages.Select(document =>
                        document.DocumentNode.QuerySelectorAll(linkPathSelectors.Last())
                                .Select(e => baseUrl + e.GetAttributeValue("href", null))
                                .Distinct()
                                .ToList())
                        .SelectMany(p => p);

        targetLinks.UnionWith(newLinks);
    }

    protected async Task<HtmlDocument[]> DownloadTargetPages(IEnumerable<string> links)
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


    HttpClient httpClient = new HttpClient(new SocketsHttpHandler()
    {
        MaxConnectionsPerServer = 100,
        SslOptions = new SslClientAuthenticationOptions
        {
            // Leave certs unvalidated for debugging
            RemoteCertificateValidationCallback = delegate { return true; },
        },
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(10),
        PooledConnectionLifetime = Timeout.InfiniteTimeSpan
    });

    protected async Task<HtmlDocument> GetDocumentAsync(string url)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        var page = await httpClient.GetStringAsync(url);

        var htmlDoc = new HtmlDocument();
        htmlDoc.LoadHtml(page);

        watch.Stop();

        Log.Logger.Information("Method {method}. Elapsed: {elapsed} sec",
            nameof(GetDocumentAsync),
            watch.Elapsed.TotalSeconds);

        return htmlDoc;
    }

    private async Task SaveAsync(HtmlDocument doc)
    {
        var result = GetJson(doc);
        await File.AppendAllTextAsync(filePath, JsonConvert.SerializeObject(result) + ",");
    }
}
