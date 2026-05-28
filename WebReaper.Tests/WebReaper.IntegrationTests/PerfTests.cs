using WebReaper.Builders;
using WebReaper.Domain.Parsing;
using WebReaper.IntegrationTests.Fixtures;
using WebReaper.TestServer;
using Xunit;

namespace WebReaper.IntegrationTests;

/// <summary>
/// Scale regression guard (the asserted half of the perf story; the throughput
/// numbers live in the WebReaper.Perf console harness). A 500-page crawl at
/// parallelism 20 must emit every page exactly once — this catches
/// outstanding-work-latch imbalances, dropped jobs, or duplicate emission that
/// only surface under concurrency at volume, deterministically (exact count,
/// no timing). Tagged Perf so it stays out of the fast gate.
/// </summary>
[Collection("LocalSite")]
[Trait("Category", "Perf")]
public sealed class PerfTests
{
    private readonly LocalTestSite _site;

    public PerfTests(LocalSiteFixture fixture) => _site = fixture.Site;

    [Fact]
    public async Task Large_concurrent_crawl_emits_every_page_exactly_once()
    {
        const int count = 500;
        var emitted = 0;

        await using (var engine = await ScraperEngineBuilder
            .Crawl(_site.Url($"/genlist?count={count}"))
            .Extract(new Schema { new("title", ".title") })
            .Follow("a.gen")
            .WithParallelismDegree(20)
            .Subscribe(_ => Interlocked.Increment(ref emitted))
            .StopWhenAllLinksProcessed()
            .BuildAsync())
        {
            await engine.RunAsync();
        }

        Assert.Equal(count, emitted);   // no loss, no duplication at scale
    }
}
