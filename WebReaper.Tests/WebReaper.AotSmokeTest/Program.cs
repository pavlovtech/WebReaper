// ADR 0008 step 4 (re-scoped) AOT smoke test. Exercises ONLY the
// Newtonsoft-free PRODUCTION path under PublishAot with the IL2026/IL3050
// family promoted to build errors: WebReaperJson config + Job round-trip,
// the typed JsonObject fold terminal (SchemaFold.ExtractAsync)
// over a trivial in-memory backend, and the JsonObject file formats. Exits
// non-zero on any assertion failure; the build fails on any trim/AOT warning.

using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using WebReaper.Core.Loaders.Concrete;
using WebReaper.Core.Parser.Abstract;
using WebReaper.Core.Parser.Concrete;
using WebReaper.Domain;
using WebReaper.Domain.PageActions;
using WebReaper.Domain.Parsing;
using WebReaper.Domain.Selectors;
using WebReaper.Serialization;
using WebReaper.Sinks.Concrete;

var failures = new List<string>();
void Check(bool ok, string label)
{
    Console.WriteLine($"  {(ok ? "PASS" : "FAIL")}  {label}");
    if (!ok) failures.Add(label);
}

// 1. Production WebReaperJson — ScraperConfig round-trip (polymorphic
//    Schema/SchemaElement, ImmutableQueue<LinkPathSelector>, the PageAction sum).
var config = new ScraperConfig(
    ParsingScheme: new Schema
    {
        new Schema("post") { Children = { new SchemaElement("views", ".v", DataType.Integer) } }
    },
    LinkPathSelectors: ImmutableQueue.CreateRange(new[]
    {
        new LinkPathSelector("a.cat", null, PageType.Static),
        new LinkPathSelector("a.item", "a.next", PageType.Dynamic,
            new List<PageAction> { new PageAction.WaitForSelector("div.x", 5000) })
    }),
    StartUrls: new[] { "https://x.test/s" },
    UrlBlackList: new[] { "https://x.test/skip" },
    PageCrawlLimit: 7,
    StartPageType: PageType.Dynamic,
    PageActions: new List<PageAction> { new PageAction.Click("#go") },
    Headless: false,
    StopWhenDrained: true);

var gotConfig = WebReaperJson.DeserializeConfig(WebReaperJson.SerializeConfig(config));
Check(gotConfig.PageCrawlLimit == 7 && gotConfig.StartPageType == PageType.Dynamic,
    "config scalars + string enum round-trip");
Check(gotConfig.LinkPathSelectors.Count() == 2
      && gotConfig.LinkPathSelectors.Last().PaginationSelector == "a.next",
    "ImmutableQueue<LinkPathSelector> round-trip");
Check(gotConfig.PageActions![0] is PageAction.Click { Selector: "#go" },
    "PageAction closed-sum arm round-trips");

// 1b. ADR-0074: Press arm codec round-trip.
var pressConfig = new ScraperConfig(
    ParsingScheme: null,
    LinkPathSelectors: ImmutableQueue<LinkPathSelector>.Empty,
    StartUrls: new[] { "https://x.test/s" },
    UrlBlackList: Array.Empty<string>(),
    PageCrawlLimit: 1,
    StartPageType: PageType.Static,
    PageActions: new List<PageAction> { new PageAction.Press("Control+A") },
    Headless: true,
    StopWhenDrained: false);
var gotPressConfig = WebReaperJson.DeserializeConfig(WebReaperJson.SerializeConfig(pressConfig));
Check(gotPressConfig.PageActions![0] is PageAction.Press { Key: "Control+A" },
    "PageAction.Press codec round-trip (ADR-0074)");

// 1c. ADR-0074: ScrollIntoView arm codec round-trip.
var sivConfig = new ScraperConfig(
    ParsingScheme: null,
    LinkPathSelectors: ImmutableQueue.CreateRange(new[]
    {
        new LinkPathSelector("a.item", null, PageType.Static)
    }),
    StartUrls: new[] { "https://x.test/s" },
    UrlBlackList: Array.Empty<string>(),
    PageCrawlLimit: int.MaxValue,
    StartPageType: PageType.Static,
    PageActions: new List<PageAction> { new PageAction.ScrollIntoView("#target") },
    Headless: true,
    StopWhenDrained: false);
var sivGot = WebReaperJson.DeserializeConfig(WebReaperJson.SerializeConfig(sivConfig));
Check(sivGot.PageActions![0] is PageAction.ScrollIntoView { Selector: "#target" },
    "PageAction.ScrollIntoView round-trip (ADR-0074)");

// 1d. ADR-0074: Fill arm round-trip (codec + AOT-clean).
var fillConfig = new ScraperConfig(
    ParsingScheme: null,
    LinkPathSelectors: ImmutableQueue.CreateRange(new[]
    {
        new LinkPathSelector("a.item", null, PageType.Static)
    }),
    StartUrls: new[] { "https://x.test/s" },
    UrlBlackList: Array.Empty<string>(),
    PageCrawlLimit: 1,
    StartPageType: PageType.Static,
    PageActions: new List<PageAction> { new PageAction.Fill("input#q", "cats") },
    Headless: false,
    StopWhenDrained: false);
var gotFillConfig = WebReaperJson.DeserializeConfig(WebReaperJson.SerializeConfig(fillConfig));
Check(gotFillConfig.PageActions![0] is PageAction.Fill { Selector: "input#q", Value: "cats" },
    "PageAction.Fill arm round-trips (ADR-0074, AOT-clean)");
Check(((Schema)gotConfig.ParsingScheme!.Children[0]).Children[0].Type == DataType.Integer,
    "polymorphic Schema/SchemaElement round-trip");

// 2. Production WebReaperJson — Job round-trip (ADR-0005 closure).
var job = new Job("https://x.test/p",
    ImmutableQueue.CreateRange(new[] { new LinkPathSelector("a.x", null, PageType.Static) }),
    ImmutableQueue.CreateRange(new[] { "https://x.test" }),
    PageType.Dynamic,
    new List<PageAction> { new PageAction.Click("#b") });
var gotJob = WebReaperJson.DeserializeJob(WebReaperJson.SerializeJob(job));
Check(gotJob.LinkPathSelectors.Single().Selector == "a.x"
      && gotJob.ParentBacklinks.Single() == "https://x.test"
      && gotJob.PageActions![0] is PageAction.Click { Selector: "#b" },
    "Job round-trip with type fidelity (ADR-0005 closed)");

// 3. Production typed fold terminal over a trivial Newtonsoft-free backend.
var parser = new SchemaFold<KvNode>(new KvBackend(), NullLogger.Instance);
JsonObject parsed = await parser.ExtractAsync(
    "title=Hello\nviews=42",
    new Schema
    {
        new SchemaElement("title", "title", DataType.String),
        new SchemaElement("views", "views", DataType.Integer)
    });
Check(parsed["title"]!.GetValue<string>() == "Hello"
      && parsed["views"]!.GetValue<int>() == 42,
    "typed JsonObject fold terminal (no Newtonsoft)");

// 3b. Production JSON backend — the ADR-0008 JSONPath cursor on the
//     Newtonsoft-free path. Before the JSONPath→STJ migration this reaches
//     Newtonsoft JToken (Parse/SelectToken/SelectTokens) and FAILS the
//     publish (IL2104/IL3053 whole-assembly rollup, promoted to error);
//     after, JsonSchemaBackend is JsonNode-only and AOT-clean. Exercises the
//     full used dialect: relative dotted, $-rooted, and $.a[*] wildcard.
var jsonParser = new SchemaFold<JsonNode>(new JsonSchemaBackend(), NullLogger.Instance);
JsonObject jp = await jsonParser.ExtractAsync(
    @"{ ""post"": { ""title"": ""Hi"", ""views"": 42 }, ""tags"": [ ""a"", ""b"" ] }",
    new Schema
    {
        new SchemaElement("title", "post.title", DataType.String),
        new SchemaElement("views", "$.post.views", DataType.Integer),
        new SchemaElement("tags", "$.tags[*]", DataType.String) { IsList = true }
    });
Check(jp["title"]!.GetValue<string>() == "Hi"
      && jp["views"]!.GetValue<int>() == 42
      && jp["tags"]!.AsArray().Count == 2
      && jp["tags"]![0]!.GetValue<string>() == "a",
    "JSON backend JSONPath cursor (Newtonsoft-free, AOT-clean)");

// 4. Production JsonObject file formats.
parsed["url"] = "https://x.test/p";
Check(new JsonLinesFormat().FormatRow(parsed)
      == "{\"title\":\"Hello\",\"views\":42,\"url\":\"https://x.test/p\"}",
    "JsonLinesFormat over JsonObject");
Check(new CsvFormat().Header(parsed) == "title,views,url"
      && new CsvFormat().FormatRow(parsed) == "\"Hello\",\"42\",\"https://x.test/p\"",
    "CsvFormat over JsonObject");

// 5. ADR-0040: the Markdown content extractor — the second adapter of
//    IContentExtractor, no Schema, AngleSharp DOM walker, JsonObject
//    terminal. AOT-clean: no reflection, no dynamic, no Activator —
//    just JsonValue.Create(string) over a StringBuilder render.
var markdown = new MarkdownContentExtractor();
var md = await markdown.ExtractAsync(
    "<html><head><title>Head</title></head><body>" +
    "<article><h1>Hello</h1><p>This <strong>is</strong> a paragraph.</p>" +
    "<ul><li>One</li><li>Two</li></ul></article>" +
    "<footer>Strip me.</footer></body></html>", schema: null);
Check(md["title"]!.GetValue<string>() == "Hello"
      && md["markdown"]!.GetValue<string>().Contains("# Hello")
      && md["markdown"]!.GetValue<string>().Contains("**is**")
      && md["markdown"]!.GetValue<string>().Contains("- One")
      && !md["markdown"]!.GetValue<string>().Contains("Strip me"),
    "MarkdownContentExtractor (no-schema, AOT-clean)");

// 6. ADR-0041: the in-memory page cache — the cache-aside collaborator on
//    PageLoader. AOT-clean: ConcurrentDictionary<string, struct> with no
//    reflection paths.
var pageCache = new InMemoryPageCache(TimeSpan.FromMinutes(1));
await pageCache.WriteAsync("https://x.test/p", WebReaper.Domain.Selectors.PageType.Static, "<cached/>", default);
var staticHit = await pageCache.TryReadAsync("https://x.test/p", WebReaper.Domain.Selectors.PageType.Static, default);
var dynamicMiss = await pageCache.TryReadAsync("https://x.test/p", WebReaper.Domain.Selectors.PageType.Dynamic, default);
Check(staticHit == "<cached/>" && dynamicMiss is null,
    "InMemoryPageCache hit/miss with (url, pageType) keying (ADR-0041)");

Console.WriteLine();
if (failures.Count == 0) { Console.WriteLine("AOT SMOKE: ALL PASS"); return 0; }
Console.WriteLine($"AOT SMOKE: {failures.Count} FAILURE(S)");
return 1;

// A deliberately trivial, Newtonsoft-free ISchemaBackend (line-based
// key=value), mirroring SchemaFoldTests' KeyValueBackend: ExtractRaw returns a
// string, so the typed terminal's JToken bridge is never reached.
file sealed class KvNode
{
    public ILookup<string, string>? Root { get; init; }
    public string? Value { get; init; }
}

file sealed class KvBackend : ISchemaBackend<KvNode>
{
    public Task<KvNode> RootAsync(string content) => Task.FromResult(new KvNode
    {
        Root = content
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => l.Split('=', 2))
            .ToLookup(p => p[0], p => p[1])
    });

    public IEnumerable<KvNode> SelectMany(KvNode scope, string selector)
        => scope.Root![selector].Select(v => new KvNode { Value = v });

    public KvNode? SelectOne(KvNode scope, string selector)
        => scope.Root![selector].FirstOrDefault() is { } v ? new KvNode { Value = v } : null;

    public object? ExtractRaw(KvNode node, SchemaElement element) => node.Value;
}
