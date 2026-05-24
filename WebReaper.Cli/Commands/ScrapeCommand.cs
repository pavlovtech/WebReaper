using System.Text.Json;
using System.Text.Json.Nodes;
using WebReaper.Builders;
using WebReaper.Cdp;
using WebReaper.Domain.Parsing;
using WebReaper.Sinks.Models;

namespace WebReaper.Cli.Commands;

// `webreaper scrape <url>` — the funnel's primitive call. Markdown to
// stdout by default; JSON when --schema is supplied.
internal static class ScrapeCommand
{
    public static async Task<int> RunAsync(ParsedArgs args)
    {
        if (args.Positional.Count < 1)
            throw new CliException("Missing <url>. Usage: webreaper scrape <url> [flags]");

        var url = args.Positional[0];
        var schemaPath = args.GetFlag("schema");
        var output = args.GetFlag("output");
        var maxAge = args.GetTimeSpanFlag("max-age");
        var follow = args.GetFlag("follow");
        var cdpUrl = args.GetFlag("browser-cdp-url");
        var browser = args.HasFlag("browser") || cdpUrl is not null;

        // ADR-0040: AsMarkdown is the default; Extract(schema) is the
        // upgrade. A future LLM extractor (ADR-0044) will land as a
        // third terminal (e.g. --as llm).
        var seed = browser
            ? ScraperEngineBuilder.CrawlWithBrowser(url)
            : ScraperEngineBuilder.Crawl(url);

        var builder = schemaPath is not null
            ? seed.Extract(LoadSchema(schemaPath))
            : seed.AsMarkdown();

        // ADR-0055 layered auto-spawn:
        //   1. BYO via --browser-cdp-url → WithCdpPageLoader(url)
        //   2. --browser only → managed Chromium spawn via WithCdpPageLoader(CdpLaunchOptions)
        //   3. (future) auto-escalate on bot-check detection — Hybrid C UX
        if (cdpUrl is not null)
        {
            builder = builder.WithCdpPageLoader(cdpUrl);
        }
        else if (browser)
        {
            builder = builder.WithCdpPageLoader(new CdpLaunchOptions
            {
                Headless = true,
            });
        }

        if (follow is not null) builder = builder.Follow(follow);
        if (maxAge is { } age) builder = builder.WithMaxAge(age);

        // We collect with a small in-memory sink — the CLI returns the
        // page's record(s), one JSON object per line, regardless of
        // Markdown vs Schema (Markdown emits a {title, markdown} record).
        var records = new List<ParsedData>();
        builder = builder.Subscribe(records.Add);
        builder = builder.StopWhenAllLinksProcessed();

        var engine = await builder.BuildAsync();
        await engine.RunAsync();

        await Emit(records, output);
        return 0;
    }

    private static async Task Emit(List<ParsedData> records, string? output)
    {
        // Default: write to stdout. With --output, write to file.
        // Single record → output its Data JSON; multiple → JSON Lines.
        var sb = new System.Text.StringBuilder();
        foreach (var r in records)
        {
            sb.Append(r.Data.ToJsonString());
            sb.Append('\n');
        }

        var text = sb.ToString().TrimEnd('\n');

        if (output is not null)
            await File.WriteAllTextAsync(output, text);
        else
            Console.WriteLine(text);
    }

    private static Schema LoadSchema(string path)
    {
        if (!File.Exists(path))
            throw new CliException($"Schema file not found: {path}");

        string content;
        try { content = File.ReadAllText(path); }
        catch (Exception ex)
        {
            throw new CliException($"Failed to read schema file '{path}': {ex.Message}");
        }

        JsonNode? root;
        try { root = JsonNode.Parse(content); }
        catch (JsonException ex)
        {
            throw new CliException($"Schema file '{path}' is not valid JSON: {ex.Message}");
        }

        if (root is not JsonObject obj)
            throw new CliException(
                $"Schema file '{path}' must contain a JSON object at the root.");

        var schema = BuildSchema(obj);
        return schema;
    }

    private static Schema BuildSchema(JsonObject obj)
    {
        // Recursive parse of the schema JSON shape into the library's
        // Schema/SchemaElement records. The shape pinned in ADR-0043:
        //   { field, selector?, type?, attr?, is_list?, children? }
        //
        // An object with children is a Schema (a nested container);
        // a leaf with no children is a SchemaElement.

        var children = obj["children"] as JsonArray;

        if (children is null || children.Count == 0)
        {
            // Leaf.
            return WrapAsSchema(BuildElement(obj));
        }

        // Container.
        var field = obj["field"]?.GetValue<string>();
        var selector = obj["selector"]?.GetValue<string>();
        var isList = obj["is_list"]?.GetValue<bool>() ?? false;

        var container = field is not null
            ? new Schema(field) { Selector = selector ?? string.Empty, IsList = isList }
            : new Schema();

        foreach (var child in children)
        {
            if (child is not JsonObject childObj)
                throw new CliException("Schema children must be objects.");
            container.Add(BuildElement(childObj));
        }

        return container;
    }

    private static SchemaElement BuildElement(JsonObject obj)
    {
        var field = obj["field"]?.GetValue<string>()
            ?? throw new CliException("Schema element is missing 'field'.");

        var children = obj["children"] as JsonArray;
        if (children is not null && children.Count > 0)
        {
            return BuildSchema(obj);
        }

        var selector = obj["selector"]?.GetValue<string>() ?? string.Empty;
        var attr = obj["attr"]?.GetValue<string>();
        var isList = obj["is_list"]?.GetValue<bool>() ?? false;
        var type = ParseDataType(obj["type"]?.GetValue<string>());

        var element = new SchemaElement(field, selector)
        {
            Type = type,
            IsList = isList
        };

        if (attr is not null) element.Attr = attr;

        return element;
    }

    private static Schema WrapAsSchema(SchemaElement element)
    {
        if (element is Schema schema) return schema;
        return new Schema { element };
    }

    private static DataType? ParseDataType(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        return raw.ToLowerInvariant() switch
        {
            "string" => DataType.String,
            "integer" or "int" => DataType.Integer,
            "float" or "double" or "decimal" => DataType.Float,
            "boolean" or "bool" => DataType.Boolean,
            "datetime" or "date" => DataType.DataTime,
            _ => throw new CliException(
                $"Unknown schema type '{raw}'. Valid: string, integer, float, boolean, datetime.")
        };
    }
}
