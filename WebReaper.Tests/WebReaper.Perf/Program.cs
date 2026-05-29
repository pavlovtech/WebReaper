// WebReaper throughput harness. Not a gated test — a `dotnet run` tool that
// measures end-to-end crawl speed (load -> extract -> emit), the number that
// matters for "how fast does it scrape", not a micro-benchmark of one method.
//
// Local synthetic mode (default) — the engine ceiling, no network:
//   dotnet run -c Release --project WebReaper.Tests/WebReaper.Perf [pageCount]
//
// Real-site mode — network-bound, any target (first arg is an http(s) URL):
//   dotnet run -c Release --project WebReaper.Tests/WebReaper.Perf \
//       <url> <follow-css> [field=css ...]
//   e.g. ... "https://books.toscrape.com/catalogue/page-1.html" \
//            "article.product_pod h3 a" "title=.product_main h1" "price=.price_color"
//   With no field=css pairs it extracts markdown. Compare the real pages/sec
//   against the local ceiling to see how much is the network vs the engine.
//
// There is no BenchmarkDotNet dependency.

using System.Diagnostics;
using WebReaper.Builders;
using WebReaper.Cdp;
using WebReaper.Domain.Parsing;
using WebReaper.TestServer;

// Real-site mode dispatch: first arg is an http(s) URL.
if (args is [var maybeUrl, ..] &&
    (maybeUrl.StartsWith("http://") || maybeUrl.StartsWith("https://")))
{
    await BenchmarkRealSite(args);
    return;
}

var pageCount = args.Length > 0 && int.TryParse(args[0], out var n) ? n : 300;

await using var site = await LocalTestSite.StartAsync();
Console.WriteLine($"Local test site: {site.BaseUrl}");
Console.WriteLine($"\nHTTP throughput — crawling {pageCount} leaf pages at varying parallelism:\n");
Console.WriteLine($"{"parallelism",-13}{"pages",-8}{"elapsed (s)",-14}{"pages/sec",-12}{"heap (MB)",-12}");
Console.WriteLine(new string('-', 59));

foreach (var degree in new[] { 1, 10, 20, 50 })
{
    var (pages, elapsed) = await CrawlHttp(site, pageCount, degree);
    // Managed heap is reliable cross-platform (Process.PeakWorkingSet64 reads 0
    // on macOS); the growth signal is what flags a leak across degrees.
    var heapMb = GC.GetTotalMemory(forceFullCollection: false) / (1024.0 * 1024.0);
    Console.WriteLine(
        $"{degree,-13}{pages,-8}{elapsed.TotalSeconds,-14:F2}{pages / elapsed.TotalSeconds,-12:F0}{heapMb,-12:F0}");
}

// HTTP vs CDP per-page cost on a small page set (browser is ~orders slower per
// page; this quantifies it). Guarded — skips gracefully without a browser.
const int browserSample = 15;
Console.WriteLine($"\nHTTP vs CDP (browser) — {browserSample} pages, parallelism 5:\n");
var (_, httpElapsed) = await CrawlHttp(site, browserSample, 5);
Console.WriteLine($"  HTTP : {httpElapsed.TotalSeconds,6:F2} s  ({browserSample / httpElapsed.TotalSeconds:F0} pages/sec)");
try
{
    var cdpElapsed = await CrawlCdp(site, browserSample, 5);
    Console.WriteLine($"  CDP  : {cdpElapsed.TotalSeconds,6:F2} s  ({browserSample / cdpElapsed.TotalSeconds:F2} pages/sec)");
}
catch (Exception ex)
{
    Console.WriteLine($"  CDP  : skipped ({ex.GetType().Name}: {ex.Message}) — no usable Chromium?");
}

static async Task<(int pages, TimeSpan elapsed)> CrawlHttp(LocalTestSite site, int count, int degree)
{
    var pages = 0;
    var sw = Stopwatch.StartNew();
    await using (var engine = await ScraperEngineBuilder
        .Crawl(site.Url($"/genlist?count={count}"))
        .Extract(new Schema { new("title", ".title") })
        .Follow("a.gen")
        .WithParallelismDegree(degree)
        .Subscribe(_ => Interlocked.Increment(ref pages))
        .StopWhenAllLinksProcessed()
        .BuildAsync())
    {
        await engine.RunAsync();
    }
    sw.Stop();
    return (pages, sw.Elapsed);
}

static async Task<TimeSpan> CrawlCdp(LocalTestSite site, int count, int degree)
{
    var sw = Stopwatch.StartNew();
    await using (var engine = await ScraperEngineBuilder
        .CrawlWithBrowser(site.Url($"/genlist?count={count}"))
        .Extract(new Schema { new("title", ".title") })
        .WithCdpPageLoader(new CdpLaunchOptions { Headless = true })
        .FollowWithBrowser("a.gen")
        .WithParallelismDegree(degree)
        .StopWhenAllLinksProcessed()
        .BuildAsync())
    {
        await engine.RunAsync();
    }
    sw.Stop();
    return sw.Elapsed;
}

// ---- Real-site mode -------------------------------------------------------

static async Task BenchmarkRealSite(string[] args)
{
    var url = args[0];
    if (args.Length < 2)
    {
        Console.Error.WriteLine(
            "Usage: <url> <follow-css> [field=css ...]\n" +
            "  e.g. \"https://books.toscrape.com/catalogue/page-1.html\" " +
            "\"article.product_pod h3 a\" \"title=.product_main h1\"");
        return;
    }
    var followSelector = args[1];

    // Build the extraction schema from field=css pairs (split on the first '=').
    // No pairs => markdown extraction.
    var schema = new Schema();
    foreach (var pair in args.Skip(2))
    {
        var eq = pair.IndexOf('=');
        var field = eq > 0 ? pair[..eq] : "";
        var selector = eq > 0 ? pair[(eq + 1)..] : "";
        if (string.IsNullOrWhiteSpace(field) || string.IsNullOrWhiteSpace(selector))
        {
            Console.Error.WriteLine($"  (ignoring malformed field spec: '{pair}'; want field=css)");
            continue;
        }
        schema.Add(new SchemaElement(field, selector));
    }

    var extract = schema.Count > 0 ? string.Join(", ", schema.Select(e => e.Field)) : "markdown";
    Console.WriteLine($"Real-site (network-bound) -- {url}");
    Console.WriteLine($"Follow: {followSelector}   Extract: {extract}\n");
    Console.WriteLine($"{"parallelism",-13}{"pages",-8}{"elapsed (s)",-14}{"pages/sec",-12}{"wall ms/page",-13}");
    Console.WriteLine(new string('-', 60));

    foreach (var degree in new[] { 1, 5, 10, 20 })
    {
        var (pages, elapsed) = await CrawlReal(url, followSelector, schema, degree);
        var perSec = pages == 0 ? 0 : pages / elapsed.TotalSeconds;
        var msPerPage = pages == 0 ? 0 : elapsed.TotalMilliseconds / pages;
        Console.WriteLine($"{degree,-13}{pages,-8}{elapsed.TotalSeconds,-14:F2}{perSec,-12:F1}{msPerPage,-13:F0}");
    }
    Console.WriteLine("\n(p=1 wall ms/page is the real per-page latency; higher parallelism amortizes it.)");
}

static async Task<(int pages, TimeSpan elapsed)> CrawlReal(
    string url, string followSelector, Schema schema, int degree)
{
    var pages = 0;
    var sw = Stopwatch.StartNew();
    var seed = ScraperEngineBuilder.Crawl(url);
    var builder = schema.Count > 0 ? seed.Extract(schema) : seed.AsMarkdown();
    await using (var engine = await builder
        .Follow(followSelector)
        .WithParallelismDegree(degree)
        .Subscribe(_ => Interlocked.Increment(ref pages))
        .StopWhenAllLinksProcessed()
        .BuildAsync())
    {
        await engine.RunAsync();
    }
    sw.Stop();
    return (pages, sw.Elapsed);
}
