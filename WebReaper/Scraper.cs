using System.Collections.Immutable;
using System.Net;
using AngleSharp;
using AngleSharp.Dom;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WebReaper;

// anglesharpjs
// puppeter
public class Scraper
{
    protected LinkedList<string> linkPathSelectors = new();
    protected ImmutableHashSet<string> visited = ImmutableHashSet.Create<string>();
    protected int limit = int.MaxValue;
    private string? filePath = "output.json";
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
        File.AppendAllText(filePath, "[" + Environment.NewLine);
        foreach(var item in result) {
            await Save(item);
            File.AppendAllText(filePath, Environment.NewLine);
        }
        File.AppendAllText(filePath, "]" + Environment.NewLine);
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
        if(visited.Contains(url) || visited.Count >= limit) {
            return Array.Empty<IDocument>();
        }

        var config = Configuration.Default.WithDefaultLoader();
        var context = BrowsingContext.New(config);

        var document = await context.OpenAsync(url);

        ImmutableInterlocked.Update(ref visited, old => old.Add(url));

        var links = document.QuerySelectorAll(selector.Value).Select(e => e.HyperReference(e.Attributes["href"].Value).ToString()).ToList();

        if (selector.Next == null && paginationSelector == null)
        {
            var notVisitedLinks = links.Where(l => !visited.Contains(l));

            //ImmutableInterlocked.Update(ref visited, old => old.Union(notVisitedLinks));

            var tasks = notVisitedLinks.Select(l => context.OpenAsync(l));

            var result = await Task.WhenAll(tasks);

            ImmutableInterlocked.Update(ref visited, old => old.Union(notVisitedLinks));
            return result;
        } else if(selector.Next == null && paginationSelector != null)
        {
            var notVisitedLinks = links.Where(l => !visited.Contains(l));
            //ImmutableInterlocked.Update(ref visited, old => old.Union(notVisitedLinks));

            var tasks = notVisitedLinks.Select(l => context.OpenAsync(l));

            var result = await Task.WhenAll(tasks);

            ImmutableInterlocked.Update(ref visited, old => old.Union(notVisitedLinks));

            var paginatedResult = await GetTargetPagesWithPagination(document, selector.Value);

            var output = result.Concat(paginatedResult).ToArray();

            return output;
        }

        var docs = new List<IDocument>();
        
        var jobs = links.Select(link => GetTargetPages(link, selector.Next));

        var taskResults = await Task.WhenAll(jobs);
        
        var outputResult = taskResults.SelectMany(r => r);

        return outputResult.ToArray();
    }

    private void UpdateVisited(ImmutableHashSet<string> updated)
    {
        ImmutableInterlocked.Update(ref visited, (t) => updated);
    }

    private async Task<List<IDocument>> GetTargetPagesWithPagination(IDocument document, string targetPageSelector)
    {
        var config = Configuration.Default.WithDefaultLoader();
        var context = BrowsingContext.New(config);

        var nextPageLinkNode = document.QuerySelector(paginationSelector);
        var nextPageLink = nextPageLinkNode.HyperReference(nextPageLinkNode.GetAttribute("href"));
        
        var targetPages = new List<IDocument>();

        for(int i = 0; nextPageLink != null; i++) {
            document = await context.OpenAsync(nextPageLink);

            var links = document.QuerySelectorAll(targetPageSelector).Select(e => e.HyperReference(e.Attributes["href"].Value).ToString()).ToList();

            var notVisitedLinks = links.Where(link => !visited.Contains(link));

            var tasks = notVisitedLinks.Select(link => context.OpenAsync(link));
            targetPages.AddRange(await Task.WhenAll(tasks));

            ImmutableInterlocked.Update(ref visited, old => old.Union(notVisitedLinks));

            var paginationNode = document.QuerySelector(paginationSelector);
            nextPageLink = paginationNode.HyperReference(paginationNode.GetAttribute("href"));
        }

        return targetPages;
    }

    private async Task Save(IDocument doc)
    {
        var result = GetJson(doc);
        await File.AppendAllTextAsync(filePath, JsonConvert.SerializeObject(result) + ",");
    }
}
