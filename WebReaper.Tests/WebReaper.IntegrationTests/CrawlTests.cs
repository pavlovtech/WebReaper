using System.Collections.Concurrent;
using WebReaper.Builders;
using WebReaper.Domain.Parsing;
using WebReaper.IntegrationTests.Fixtures;
using WebReaper.Sinks.Models;
using WebReaper.TestServer;
using Xunit;
using Xunit.Abstractions;

namespace WebReaper.IntegrationTests;

/// <summary>
/// Link-following coverage: Follow, Paginate, PageCrawlLimit, IgnoreUrls — all
/// against the deterministic paginated index (<c>/list?page=N</c> →
/// <c>/item/{id}</c>). Emit model (ADR-0001 CrawlOutcome): only pages whose
/// selector chain is empty are *target* pages that emit ParsedData; the list /
/// transit pages do not emit. So a Follow over the 3 item links emits exactly
/// 3 records, and a Paginate across both pages emits exactly 6.
/// </summary>
[Collection("LocalSite")]
[Trait("Category", "LocalServer")]
public sealed class CrawlTests
{
    private readonly LocalTestSite _site;
    private readonly ITestOutputHelper _output;

    public CrawlTests(LocalSiteFixture fixture, ITestOutputHelper output)
    {
        _site = fixture.Site;
        _output = output;
    }

    private static Schema ItemSchema() => new() { new("title", ".title") };

    private static string[] Titles(IEnumerable<ParsedData> records) =>
        records.Select(r => r.Data["title"]!.GetValue<string>().Trim()).OrderBy(t => t).ToArray();

    [Fact]
    public async Task Follow_emits_one_record_per_leaf_page_only()
    {
        var records = new ConcurrentQueue<ParsedData>();

        await using (var engine = await ScraperEngineBuilder
            .Crawl(_site.Url("/list?page=1"))
            .Extract(ItemSchema())
            .Follow("a.item")
            .WithLogger(new TestOutputLogger(_output))
            .Subscribe(records.Enqueue)
            .StopWhenAllLinksProcessed()
            .BuildAsync())
        {
            await engine.RunAsync();
        }

        // 3 item links on page 1; the list page itself is transit (no emit).
        Assert.Equal(new[] { "Item 1", "Item 2", "Item 3" }, Titles(records));
    }

    [Fact]
    public async Task Paginate_walks_next_pages_and_emits_every_item()
    {
        var records = new ConcurrentQueue<ParsedData>();

        await using (var engine = await ScraperEngineBuilder
            .Crawl(_site.Url("/list?page=1"))
            .Extract(ItemSchema())
            .Paginate("a.item", "a.next")
            .WithLogger(new TestOutputLogger(_output))
            .Subscribe(records.Enqueue)
            .StopWhenAllLinksProcessed()
            .BuildAsync())
        {
            await engine.RunAsync();
        }

        // page 1 (items 1-3) + page 2 via a.next (items 4-6) = 6 leaf pages.
        Assert.Equal(
            new[] { "Item 1", "Item 2", "Item 3", "Item 4", "Item 5", "Item 6" },
            Titles(records));
    }

    [Fact]
    public async Task PageCrawlLimit_caps_the_crawl_below_the_full_set()
    {
        var records = new ConcurrentQueue<ParsedData>();

        await using (var engine = await ScraperEngineBuilder
            .Crawl(_site.Url("/list?page=1"))
            .Extract(ItemSchema())
            .Paginate("a.item", "a.next")
            .WithParallelismDegree(1)   // tighten the best-effort overshoot
            .PageCrawlLimit(2)
            .WithLogger(new TestOutputLogger(_output))
            .Subscribe(records.Enqueue)
            .StopWhenAllLinksProcessed()
            .BuildAsync())
        {
            await engine.RunAsync();
        }

        // Soft cap (ADR-0022): in-flight pages still finish so it may overshoot,
        // but it must constrain — strictly fewer than the unbounded 6.
        Assert.InRange(records.Count, 1, 5);
    }

    [Fact]
    public async Task IgnoreUrls_blocks_a_specific_url_from_being_enqueued()
    {
        var records = new ConcurrentQueue<ParsedData>();
        var blocked = _site.Url("/item/2");

        await using (var engine = await ScraperEngineBuilder
            .Crawl(_site.Url("/list?page=1"))
            .Extract(ItemSchema())
            .Follow("a.item")
            .IgnoreUrls(blocked)        // exact-match blocklist (ScraperEngine UrlBlackList.Contains)
            .WithLogger(new TestOutputLogger(_output))
            .Subscribe(records.Enqueue)
            .StopWhenAllLinksProcessed()
            .BuildAsync())
        {
            await engine.RunAsync();
        }

        Assert.Equal(new[] { "Item 1", "Item 3" }, Titles(records));
    }
}
