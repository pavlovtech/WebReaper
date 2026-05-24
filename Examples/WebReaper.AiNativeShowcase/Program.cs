// ADR-0040..0048 — the 10.0.0 AI-native wave on display.
//
// Run a single subcommand to see one API in action:
//
//   dotnet run --project Examples/WebReaper.AiNativeShowcase -- markdown
//   dotnet run --project Examples/WebReaper.AiNativeShowcase -- map
//   dotnet run --project Examples/WebReaper.AiNativeShowcase -- sourcegen
//   dotnet run --project Examples/WebReaper.AiNativeShowcase -- llm
//   dotnet run --project Examples/WebReaper.AiNativeShowcase -- router
//   dotnet run --project Examples/WebReaper.AiNativeShowcase -- changetrack
//
// Network: `markdown`, `map` and `sourcegen` hit `alexpavlov.dev`
// (the same site the integration tests target). `llm`, `router` and
// `changetrack` use a deterministic in-process StubChatClient so the
// demo runs offline.

using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using WebReaper.AI;
using WebReaper.AiNativeShowcase;
using WebReaper.Builders;
using WebReaper.Core.Mapping;
using WebReaper.Domain.Parsing;

string command = args.Length == 0 ? "markdown" : args[0];

switch (command)
{
    case "markdown":     await Markdown();    break;
    case "map":          await Map();         break;
    case "sourcegen":    await SourceGen();   break;
    case "llm":          await Llm();         break;
    case "router":       await Router();      break;
    case "changetrack":  await ChangeTrack(); break;
    default:
        Console.Error.WriteLine($"Unknown subcommand '{command}'. " +
            "Expected one of: markdown, map, sourcegen, llm, router, changetrack.");
        Environment.Exit(2);
        break;
}

// ADR-0040 — `.AsMarkdown()` is the second ICrawlSeed terminal. No schema;
// the crawl emits `{ url, title, markdown }` per page. The funnel's
// LLM-ready no-config wedge.
static async Task Markdown()
{
    var engine = await ScraperEngineBuilder
        .Crawl("https://www.alexpavlov.dev/blog")
        .AsMarkdown()
        .WriteToJsonFile("markdown.jsonl")
        .PageCrawlLimit(3)
        .LogToConsole()
        .BuildAsync();

    await engine.RunAsync();
    Console.WriteLine("markdown.jsonl written.");
}

// ADR-0042 — `ScraperEngineBuilder.MapAsync` is the URL-discovery
// one-liner. Parses robots.txt for Sitemap: lines, recurses one level
// of sitemap-indexes, falls back to root-page <a href>s. No Crawl
// machinery spent — one HTTP fetch.
static async Task Map()
{
    var urls = await ScraperEngineBuilder.MapAsync(
        "https://www.alexpavlov.dev",
        new MapOptions { MaxUrls = 50 });

    Console.WriteLine($"Discovered {urls.Count} URLs:");
    foreach (var url in urls) Console.WriteLine($"  {url}");
}

// ADR-0045 — `[ScrapeSchema]` on a partial class (see Models.cs) triggers
// the Roslyn source generator to emit a compile-time `static Schema Schema`
// and a reflection-free `static Materialize(JsonObject)`. AOT-clean.
static async Task SourceGen()
{
    // The generated `BlogPost.Schema` IS the schema the fold consumes —
    // no hand-written `new Schema { new SchemaElement(...), ... }`.
    var engine = await ScraperEngineBuilder
        .CrawlWithBrowser("https://www.alexpavlov.dev/blog")
        .Extract(BlogPost.Schema)
        .Follow(".text-gray-900.transition")
        .WriteToJsonFile("sourcegen.jsonl")
        .PageCrawlLimit(3)
        .LogToConsole()
        .BuildAsync();

    await engine.RunAsync();
    Console.WriteLine("sourcegen.jsonl written.");

    // The generator also emits Materialize — round-trip from the
    // file's JSON line back to a strongly-typed BlogPost is one call,
    // no reflection.
    var firstLine = File.ReadLines("sourcegen.jsonl").First();
    var post = BlogPost.Materialize((JsonObject)JsonNode.Parse(firstLine)!);
    Console.WriteLine($"First post.Title (materialized, no reflection): {post.Title}");
}

// ADR-0044 — `.WithLlmExtractor(IChatClient)` swaps the deterministic
// fold for an LLM-backed IContentExtractor bound to
// Microsoft.Extensions.AI.Abstractions. BYO model. Here we use a stub
// IChatClient that returns canned JSON so the demo runs offline.
static async Task Llm()
{
    var stub = new StubChatClient(_ =>
        """{"title": "Hello from the LLM", "summary": "Stubbed response."}""");

    var schema = new Schema
    {
        new SchemaElement("title", "h1"),
        new SchemaElement("summary", "article"),
    };

    var engine = await ScraperEngineBuilder
        .Crawl("https://example.com/")
        .Extract(schema)
        .WithLlmExtractor(stub)
        .WriteToConsole()
        .PageCrawlLimit(1)
        .BuildAsync();

    await engine.RunAsync();
    Console.WriteLine("(stub IChatClient — replace with an OpenAI / Anthropic / Ollama " +
                      "IChatClient implementation in production.)");
}

// ADR-0046 — `ExtractionRouter` composes deterministic-first → LLM
// fallback on the IContentExtractor seam. The router runs the cheap
// fold, validates via SchemaSatisfiedValidator, and only falls through
// to the LLM when a required leaf is missing.
static async Task Router()
{
    var stub = new StubChatClient(_ =>
        """{"title": "LLM rescued the parse", "summary": "Fold missed h1; LLM filled it."}""");

    var schema = new Schema
    {
        new SchemaElement("title", "h1"),
        new SchemaElement("summary", "article"),
    };

    var engine = await ScraperEngineBuilder
        .Crawl("https://example.com/")
        .Extract(schema)
        .WithLlmFallback(stub)
        .WriteToConsole()
        .PageCrawlLimit(1)
        .BuildAsync();

    await engine.RunAsync();
    Console.WriteLine("(router: deterministic fold first; LLM only when validation fails.)");
}

// ADR-0048 — `ChangeTrackingProcessor` is an IPageProcessor on the
// page-processor pipeline. It hashes each page's Markdown extraction
// (SHA-256, robust to template noise), reads the prior hash from
// IChangeStore (InMemoryChangeStore default), and emits
// `change_status` ∈ { "new", "same", "changed" } + `previous_hash`.
static async Task ChangeTrack()
{
    var engine = await ScraperEngineBuilder
        .Crawl("https://www.alexpavlov.dev/blog")
        .AsMarkdown()
        .WithChangeTracking()                  // InMemoryChangeStore by default
        .WriteToJsonFile("changetrack.jsonl")
        .PageCrawlLimit(3)
        .LogToConsole()
        .BuildAsync();

    await engine.RunAsync();
    Console.WriteLine("changetrack.jsonl — each record has change_status (new/same/changed) " +
                      "and previous_hash. Re-run to see 'same' on unchanged pages; edit a " +
                      "live page to see 'changed'. Swap InMemoryChangeStore for a persistent " +
                      "IChangeStore for cross-run comparison.");
}
