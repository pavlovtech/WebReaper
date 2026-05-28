// WebReaper throughput harness. Not a gated test — a `dotnet run` tool that
// crawls the deterministic local site and reports pages/sec + peak memory at
// a range of parallelism degrees, plus a small HTTP-vs-CDP per-page cost row.
//
//   dotnet run -c Release --project WebReaper.Tests/WebReaper.Perf [pageCount]
//
// There is no BenchmarkDotNet dependency: this measures the end-to-end crawl
// (load → extract → emit), which is the number that matters for "how fast does
// it scrape", not a micro-benchmark of one method.

using System.Diagnostics;
using WebReaper.Builders;
using WebReaper.Cdp;
using WebReaper.Domain.Parsing;
using WebReaper.TestServer;

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
