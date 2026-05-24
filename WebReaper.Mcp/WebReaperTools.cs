using System.ComponentModel;
using System.Text;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using WebReaper.Builders;
using WebReaper.Core.Mapping;
using WebReaper.Domain.Parsing;
using WebReaper.Sinks.Models;

namespace WebReaper.Mcp;

// ADR-0049: the three MCP tools the satellite exposes — scrape, map,
// extract. Each wraps an existing library API; the tool layer is thin
// glue between the MCP-protocol JSON shape and the library's fluent
// builders.

[McpServerToolType]
public static class WebReaperTools
{
    [McpServerTool(Name = "scrape"), Description(
        "Fetch a URL and return its main content as LLM-ready Markdown. " +
        "The lowest-cost call against any site. Useful for reading a page " +
        "into context.")]
    public static async Task<string> Scrape(
        [Description("The URL to scrape.")] string url,
        [Description("Use the headless browser (for JS-rendered pages). Requires the WebReaper.Puppeteer satellite at run time. Default false.")] bool browser = false)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("URL is required.", nameof(url));

        var seed = browser
            ? ScraperEngineBuilder.CrawlWithBrowser(url)
            : ScraperEngineBuilder.Crawl(url);

        var records = new List<ParsedData>();
        var engine = await seed.AsMarkdown()
            .Subscribe(records.Add)
            .StopWhenAllLinksProcessed()
            .BuildAsync();
        await engine.RunAsync();

        var output = new StringBuilder();
        foreach (var record in records)
        {
            var title = record.Data["title"]?.GetValue<string>() ?? string.Empty;
            var md = record.Data["markdown"]?.GetValue<string>() ?? string.Empty;
            if (!string.IsNullOrEmpty(title))
                output.Append("# ").Append(title).Append("\n\n");
            output.AppendLine(md);
        }
        return output.ToString().TrimEnd();
    }

    [McpServerTool(Name = "map"), Description(
        "Discover URLs on a site via sitemap.xml + root-page link " +
        "extraction. Returns a newline-separated list of URLs.")]
    public static async Task<string> Map(
        [Description("The site root URL.")] string url,
        [Description("Optional case-insensitive substring filter on the returned URLs (e.g. \"/blog/\").")] string? search = null,
        [Description("Cap on the number of URLs to return. Default 1000.")] int maxUrls = 1000,
        [Description("Keep off-site URLs in the result. Default false.")] bool allowOffsite = false)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("URL is required.", nameof(url));

        var options = new MapOptions(
            MaxUrls: maxUrls,
            AllowOffsite: allowOffsite,
            Search: search);

        var urls = await ScraperEngineBuilder.MapAsync(url, options);
        return string.Join("\n", urls);
    }

    [McpServerTool(Name = "extract"), Description(
        "Extract structured fields from a URL using a JSON schema. The " +
        "schema mirrors the WebReaper Schema shape: " +
        "{ field, children: [ { field, selector, type, is_list }, ... ] }. " +
        "Returns the extracted record(s) as JSON Lines.")]
    public static async Task<string> Extract(
        [Description("The URL to extract from.")] string url,
        [Description("The extraction schema as JSON. See the WebReaper docs for the shape.")] string schemaJson,
        [Description("Use the headless browser (requires WebReaper.Puppeteer). Default false.")] bool browser = false)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("URL is required.", nameof(url));
        if (string.IsNullOrWhiteSpace(schemaJson))
            throw new ArgumentException("Schema JSON is required.", nameof(schemaJson));

        Schema schema;
        try
        {
            schema = ParseSchema(schemaJson);
        }
        catch (Exception ex)
        {
            throw new ArgumentException(
                $"Schema JSON is invalid: {ex.Message}", nameof(schemaJson));
        }

        var seed = browser
            ? ScraperEngineBuilder.CrawlWithBrowser(url)
            : ScraperEngineBuilder.Crawl(url);

        var records = new List<ParsedData>();
        var engine = await seed.Extract(schema)
            .Subscribe(records.Add)
            .StopWhenAllLinksProcessed()
            .BuildAsync();
        await engine.RunAsync();

        // JSON Lines — one record per line.
        return string.Join("\n", records.Select(r => r.Data.ToJsonString()));
    }

    // Tiny JSON → Schema parser (same shape the CLI accepts, ADR-0043).
    // Kept private to the satellite; the CLI has its own copy because
    // satellite ↔ CLI sharing would require a third package for one
    // <100-line function.
    private static Schema ParseSchema(string json)
    {
        var node = JsonNode.Parse(json) as JsonObject
            ?? throw new ArgumentException("Schema must be a JSON object at the root.");
        return BuildSchema(node);
    }

    private static Schema BuildSchema(JsonObject obj)
    {
        var children = obj["children"] as JsonArray;
        if (children is null || children.Count == 0)
        {
            return WrapAsSchema(BuildElement(obj));
        }
        var field = obj["field"]?.GetValue<string>();
        var selector = obj["selector"]?.GetValue<string>();
        var isList = obj["is_list"]?.GetValue<bool>() ?? false;
        var container = field is not null
            ? new Schema(field) { Selector = selector ?? string.Empty, IsList = isList }
            : new Schema();
        foreach (var child in children)
            if (child is JsonObject co) container.Add(BuildElement(co));
        return container;
    }

    private static SchemaElement BuildElement(JsonObject obj)
    {
        var field = obj["field"]?.GetValue<string>()
            ?? throw new ArgumentException("Schema element is missing 'field'.");
        var children = obj["children"] as JsonArray;
        if (children is not null && children.Count > 0)
            return BuildSchema(obj);
        var selector = obj["selector"]?.GetValue<string>() ?? string.Empty;
        var attr = obj["attr"]?.GetValue<string>();
        var isList = obj["is_list"]?.GetValue<bool>() ?? false;
        var element = new SchemaElement(field, selector)
        {
            Type = ParseDataType(obj["type"]?.GetValue<string>()),
            IsList = isList
        };
        if (attr is not null) element.Attr = attr;
        return element;
    }

    private static Schema WrapAsSchema(SchemaElement el) =>
        el is Schema s ? s : new Schema { el };

    private static DataType? ParseDataType(string? raw) => raw?.ToLowerInvariant() switch
    {
        null or "" => null,
        "string" => DataType.String,
        "integer" or "int" => DataType.Integer,
        "float" or "double" or "decimal" => DataType.Float,
        "boolean" or "bool" => DataType.Boolean,
        "datetime" or "date" => DataType.DataTime,
        _ => throw new ArgumentException(
            $"Unknown schema type '{raw}'.")
    };
}
