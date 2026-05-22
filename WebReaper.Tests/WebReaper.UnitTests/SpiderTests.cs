using System.Collections.Immutable;
using Microsoft.Extensions.Logging.Abstractions;
using WebReaper.Core.Crawling;
using WebReaper.Core.Crawling.Concrete;
using WebReaper.Core.Loaders.Abstract;
using WebReaper.Core.Parser.Concrete;
using WebReaper.Domain;
using WebReaper.Domain.Parsing;
using WebReaper.Domain.Selectors;

namespace WebReaper.UnitTests;

// ADR-0022: the reduced Spider shell, driven offline through its now-real seam
// (ISpider.CrawlAsync -> JobReport). Before this slice no test constructed the
// real Spider — emission escaped via events on the concrete class and
// termination via a thrown PageCrawlLimitException, so the live-site
// IntegrationTests were the only thing exercising it. The JobReport is now the
// test surface: a faithful wrap of the Crawl step's CrawlOutcome plus the
// loaded document, with no fan-out, no tracking, and no throw.
//
// ADR-0034: the shell's two run-scoped inputs — the headless flag and the
// parsing Schema — are ctor arguments. The shell holds no IScraperConfigStorage,
// so these tests construct it from (crawl step, loader, headless, schema)
// directly — no IScraperConfigStorage test double.
public class SpiderTests
{
    private sealed class FakeLoader(string html) : IPageLoader
    {
        public PageRequest? LastRequest { get; private set; }

        public Task<string> LoadAsync(PageRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(html);
        }
    }

    private static CrawlStep CrawlStep() =>
        new(new LinkParserByCssSelector(), new AngleSharpContentParser(NullLogger.Instance));

    private static Core.Spider.Concrete.Spider Spider(
        IPageLoader loader, Schema? schema, bool headless = true) =>
        new(CrawlStep(), loader, headless, schema);

    private static Job Job(string url, params LinkPathSelector[] chain) =>
        new(url, ImmutableQueue.CreateRange(chain), ImmutableQueue.Create<string>());

    [Fact]
    public async Task Target_page_reports_Parsed_with_the_loaded_document()
    {
        const string html = "<html><body><h1 class='t'>Hello</h1></body></html>";
        var schema = new Schema { new("title", "h1.t") };

        // 0 selectors => target page; the ctor-supplied schema drives extraction.
        var report = await Spider(new FakeLoader(html), schema)
            .CrawlAsync(Job("https://x.test/p/1"));

        var parsed = Assert.IsType<CrawlOutcome.Parsed>(report.Outcome);
        Assert.Equal("https://x.test/p/1", parsed.Data.Url);
        Assert.Equal("Hello", parsed.Data.Data["title"]?.ToString());
        Assert.Equal(html, report.Document);          // the doc the driver needs for Metadata
        Assert.Empty(report.Outcome.NextJobs);
    }

    [Fact]
    public async Task Transit_page_reports_Followed_child_jobs_and_no_parsed_data()
    {
        const string html = "<html><body>" +
                             "<a class='item' href='/a'>a</a>" +
                             "<a class='item' href='/b'>b</a></body></html>";

        var report = await Spider(new FakeLoader(html), schema: null)
            .CrawlAsync(Job("https://x.test/", new LinkPathSelector("a.item"), new LinkPathSelector("a.detail")));

        var followed = Assert.IsType<CrawlOutcome.Followed>(report.Outcome);
        Assert.Equal(new[] { "https://x.test/a", "https://x.test/b" }, followed.Next.Select(j => j.Url));
        Assert.Equal(html, report.Document);
    }

    [Fact]
    public async Task The_constructed_headless_flag_is_folded_into_every_page_request()
    {
        // ADR-0034: the shell holds the crawl's headless setting as a ctor
        // input — no longer re-read from config storage per Job — and folds it
        // into the PageRequest it builds for the loader.
        var loader = new FakeLoader("<html><body></body></html>");

        await Spider(loader, schema: null, headless: false)
            .CrawlAsync(Job("https://x.test/", new LinkPathSelector("a.item")));

        Assert.False(loader.LastRequest!.Headless);
    }
}
