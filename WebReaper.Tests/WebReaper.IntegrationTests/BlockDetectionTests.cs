using WebReaper.Builders;
using WebReaper.IntegrationTests.Fixtures;
using WebReaper.TestServer;
using Xunit;
using Xunit.Abstractions;

namespace WebReaper.IntegrationTests;

/// <summary>
/// ADR-0083 slice 2: end-to-end detect-and-tally wiring. Drives the in-process
/// Crawl driver against <c>/fail?status=403</c> — a 403 is data, not a fault,
/// since slice 1 (the page loads), so the block detector flags it (HTTP 403 =
/// High) and the driver increments
/// <see cref="WebReaper.Domain.Telemetry.RunReport.BlockedPageCount"/>. Pins
/// the Spider → JobReport → driver → RunReport path through the real engine.
/// </summary>
[Collection("LocalSite")]
[Trait("Category", "LocalServer")]
public sealed class BlockDetectionTests
{
    private readonly LocalTestSite _site;
    private readonly ITestOutputHelper _output;

    public BlockDetectionTests(LocalSiteFixture fixture, ITestOutputHelper output)
    {
        _site = fixture.Site;
        _output = output;
    }

    [Fact]
    public async Task A_403_load_is_tallied_as_a_blocked_page_in_the_run_report()
    {
        const string key = "block-403";

        await using var engine = await ScraperEngineBuilder
            .Crawl(_site.Url($"/fail?key={key}&status=403&times=99"))
            .AsMarkdown()
            .WithLogger(new TestOutputLogger(_output))
            .StopWhenAllLinksProcessed()
            .BuildAsync();

        var report = await engine.RunAsync();

        Assert.Equal(1, report.BlockedPageCount);   // the single 403 load was flagged
        Assert.Equal(1, _site.FailHits(key));        // requested once (403 is data, not retried)
    }

    [Fact]
    public async Task A_clean_200_load_is_not_tallied_as_blocked()
    {
        // Control: a normal page must leave BlockedPageCount at 0, so a passing
        // assertion above is a real signal and not a constant.
        await using var engine = await ScraperEngineBuilder
            .Crawl(_site.Url("/static"))
            .AsMarkdown()
            .WithLogger(new TestOutputLogger(_output))
            .StopWhenAllLinksProcessed()
            .BuildAsync();

        var report = await engine.RunAsync();

        Assert.Equal(0, report.BlockedPageCount);
    }
}
