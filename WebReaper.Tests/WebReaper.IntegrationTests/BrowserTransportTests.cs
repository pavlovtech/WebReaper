using System.Collections.Concurrent;
using WebReaper.Builders;
using WebReaper.Cdp;
using WebReaper.Domain.Parsing;
using WebReaper.IntegrationTests.Fixtures;
using WebReaper.Playwright;
using WebReaper.Sinks.Models;
using WebReaper.TestServer;
using Xunit;
using Xunit.Abstractions;

namespace WebReaper.IntegrationTests;

/// <summary>
/// Browser-transport coverage against <c>/spa</c> — a shell whose
/// <c>.title</c> / <c>.price</c> content is injected by JS after
/// DOMContentLoaded. The HTTP transport sees only the empty shell (the
/// negative control); Playwright and raw CDP render the script first, so they
/// extract the hydrated values. The HTTP-vs-browser delta is the whole point —
/// it proves the browser path actually executes JS rather than just fetching
/// HTML. Requires a browser (Playwright cache / system Chrome); tagged Browser
/// so the gate skips it.
/// </summary>
[Collection("LocalSite")]
[Trait("Category", "Browser")]
public sealed class BrowserTransportTests
{
    private readonly LocalTestSite _site;
    private readonly ITestOutputHelper _output;

    public BrowserTransportTests(LocalSiteFixture fixture, ITestOutputHelper output)
    {
        _site = fixture.Site;
        _output = output;
    }

    private static Schema TitleSchema() => new() { new("title", ".title") };

    [Fact]
    public async Task Http_transport_misses_js_injected_content()
    {
        var records = new ConcurrentQueue<ParsedData>();

        await using (var engine = await ScraperEngineBuilder
            .Crawl(_site.Url("/spa"))
            .Extract(TitleSchema())
            .Subscribe(records.Enqueue)
            .WithLogger(new TestOutputLogger(_output))
            .StopWhenAllLinksProcessed()
            .BuildAsync())
        {
            await engine.RunAsync();
        }

        // The page is still a target page, so a record emits — but the title
        // selector matched nothing in the un-executed shell.
        var rec = Assert.Single(records);
        Assert.DoesNotContain("Hydrated Title", rec.Data.ToJsonString());
    }

    [Fact]
    public async Task Playwright_transport_renders_js_then_extracts()
    {
        var records = new ConcurrentQueue<ParsedData>();

        await using (var engine = await ScraperEngineBuilder
            .CrawlWithBrowser(_site.Url("/spa"))
            .Extract(TitleSchema())
            .WithPlaywrightPageLoader()
            .Subscribe(records.Enqueue)
            .WithLogger(new TestOutputLogger(_output))
            .StopWhenAllLinksProcessed()
            .BuildAsync())
        {
            await engine.RunAsync();
        }

        var rec = Assert.Single(records);
        Assert.Equal("Hydrated Title", rec.Data["title"]!.GetValue<string>().Trim());
    }

    [Fact]
    public async Task Cdp_transport_renders_js_then_extracts()
    {
        var records = new ConcurrentQueue<ParsedData>();

        await using (var engine = await ScraperEngineBuilder
            .CrawlWithBrowser(_site.Url("/spa"))
            .Extract(TitleSchema())
            .WithCdpPageLoader(new CdpLaunchOptions { Headless = true })
            .Subscribe(records.Enqueue)
            .WithLogger(new TestOutputLogger(_output))
            .StopWhenAllLinksProcessed()
            .BuildAsync())
        {
            await engine.RunAsync();
        }

        var rec = Assert.Single(records);
        Assert.Equal("Hydrated Title", rec.Data["title"]!.GetValue<string>().Trim());
    }
}
