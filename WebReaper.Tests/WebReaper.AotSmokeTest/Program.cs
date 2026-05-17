// ADR 0008 step 4 (re-scoped) AOT smoke test. Exercises ONLY the
// Newtonsoft-free PRODUCTION path under PublishAot with the IL2026/IL3050
// family promoted to build errors: WebReaperJson config + Job round-trip,
// the typed JsonObject fold terminal (SchemaContentParser.ParseToJsonAsync)
// over a trivial in-memory backend, and the JsonObject file formats. Exits
// non-zero on any assertion failure; the build fails on any trim/AOT warning.

using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
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
//    Schema/SchemaElement, ImmutableQueue<LinkPathSelector>, object[]).
var config = new ScraperConfig(
    ParsingScheme: new Schema
    {
        new Schema("post") { Children = { new SchemaElement("views", ".v", DataType.Integer) } }
    },
    LinkPathSelectors: ImmutableQueue.CreateRange(new[]
    {
        new LinkPathSelector("a.cat", null, PageType.Static),
        new LinkPathSelector("a.item", "a.next", PageType.Dynamic,
            new List<PageAction> { new(PageActionType.WaitForSelector, "div.x") })
    }),
    StartUrls: new[] { "https://x.test/s" },
    UrlBlackList: new[] { "https://x.test/skip" },
    PageCrawlLimit: 7,
    StartPageType: PageType.Dynamic,
    PageActions: new List<PageAction> { new(PageActionType.Click, "#go", 42) },
    Headless: false,
    StopWhenDrained: true);

var gotConfig = WebReaperJson.DeserializeConfig(WebReaperJson.SerializeConfig(config));
Check(gotConfig.PageCrawlLimit == 7 && gotConfig.StartPageType == PageType.Dynamic,
    "config scalars + string enum round-trip");
Check(gotConfig.LinkPathSelectors.Count() == 2
      && gotConfig.LinkPathSelectors.Last().PaginationSelector == "a.next",
    "ImmutableQueue<LinkPathSelector> round-trip");
Check(Convert.ToInt32(gotConfig.PageActions![0].Parameters[1]) == 42,
    "object[] int survives (Convert.ToInt32 == 42)");
Check(((Schema)gotConfig.ParsingScheme!.Children[0]).Children[0].Type == DataType.Integer,
    "polymorphic Schema/SchemaElement round-trip");

// 2. Production WebReaperJson — Job round-trip (ADR-0005 closure).
var job = new Job("https://x.test/p",
    ImmutableQueue.CreateRange(new[] { new LinkPathSelector("a.x", null, PageType.Static) }),
    ImmutableQueue.CreateRange(new[] { "https://x.test" }),
    PageType.Dynamic,
    new List<PageAction> { new(PageActionType.Click, "#b", 9) });
var gotJob = WebReaperJson.DeserializeJob(WebReaperJson.SerializeJob(job));
Check(gotJob.LinkPathSelectors.Single().Selector == "a.x"
      && gotJob.ParentBacklinks.Single() == "https://x.test"
      && Convert.ToInt32(gotJob.PageActions![0].Parameters[1]) == 9,
    "Job round-trip with type fidelity (ADR-0005 closed)");

// 3. Production typed fold terminal over a trivial Newtonsoft-free backend.
var parser = new SchemaContentParser<KvNode>(new KvBackend(), NullLogger.Instance);
JsonObject parsed = await parser.ParseToJsonAsync(
    "title=Hello\nviews=42",
    new Schema
    {
        new SchemaElement("title", "title", DataType.String),
        new SchemaElement("views", "views", DataType.Integer)
    });
Check(parsed["title"]!.GetValue<string>() == "Hello"
      && parsed["views"]!.GetValue<int>() == 42,
    "typed JsonObject fold terminal (no Newtonsoft)");

// 4. Production JsonObject file formats.
parsed["url"] = "https://x.test/p";
Check(new JsonLinesFormat().FormatRow(parsed)
      == "{\"title\":\"Hello\",\"views\":42,\"url\":\"https://x.test/p\"}",
    "JsonLinesFormat over JsonObject");
Check(new CsvFormat().Header(parsed) == "title,views,url"
      && new CsvFormat().FormatRow(parsed) == "\"Hello\",\"42\",\"https://x.test/p\"",
    "CsvFormat over JsonObject");

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
