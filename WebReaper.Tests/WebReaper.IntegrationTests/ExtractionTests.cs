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
/// Extraction-mode coverage against the deterministic local site: schema (CSS
/// selectors) and markdown. Every assertion checks an *exact* value — the
/// local fixtures are fixed-content, so "more than one record" is replaced by
/// "this field equals this string".
///
/// Tests await <c>RunAsync()</c> to completion under
/// <c>StopWhenAllLinksProcessed()</c> (the CLI's own pattern) rather than the
/// legacy <c>Task.Delay</c> dance — finite crawls return deterministically.
/// </summary>
[Collection("LocalSite")]
[Trait("Category", "LocalServer")]
public sealed class ExtractionTests
{
    private readonly LocalTestSite _site;
    private readonly ITestOutputHelper _output;

    public ExtractionTests(LocalSiteFixture fixture, ITestOutputHelper output)
    {
        _site = fixture.Site;
        _output = output;
    }

    private static Schema ProductSchema() => new()
    {
        new("title", ".title"),
        new("price", ".price"),
        new("description", ".description"),
    };

    [Fact]
    public async Task Schema_extracts_exact_field_values_from_a_static_page()
    {
        var records = new ConcurrentQueue<ParsedData>();

        await using (var engine = await ScraperEngineBuilder
            .Crawl(_site.Url("/static"))
            .Extract(ProductSchema())
            .WithLogger(new TestOutputLogger(_output))
            .Subscribe(records.Enqueue)
            .StopWhenAllLinksProcessed()
            .BuildAsync())
        {
            await engine.RunAsync();
        }

        var rec = Assert.Single(records);
        Assert.Equal("Widget Pro 3000", rec.Data["title"]!.GetValue<string>().Trim());
        Assert.Equal("$49.99", rec.Data["price"]!.GetValue<string>().Trim());
        Assert.Equal("A deterministic test product.", rec.Data["description"]!.GetValue<string>().Trim());
        // ADR-0031: the page URL is folded into Data under "url".
        Assert.Equal(_site.Url("/static"), rec.Data["url"]!.GetValue<string>());
    }

    [Fact]
    public async Task Markdown_extractor_captures_the_page_text()
    {
        var records = new ConcurrentQueue<ParsedData>();

        await using (var engine = await ScraperEngineBuilder
            .Crawl(_site.Url("/static"))
            .AsMarkdown()
            .WithLogger(new TestOutputLogger(_output))
            .Subscribe(records.Enqueue)
            .StopWhenAllLinksProcessed()
            .BuildAsync())
        {
            await engine.RunAsync();
        }

        var rec = Assert.Single(records);
        // Field-name-agnostic: the rendered markdown payload must carry the
        // page's heading text somewhere in the emitted record.
        Assert.Contains("Widget Pro 3000", rec.Data.ToJsonString());
    }
}
