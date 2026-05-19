using System.Collections.Immutable;
using Microsoft.Extensions.Logging.Abstractions;
using WebReaper.ConfigStorage.Abstract;
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
public class SpiderTests
{
    private sealed class FakeLoader(string html) : IPageLoader
    {
        public Task<string> LoadAsync(PageRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(html);
    }

    private sealed class FakeConfig(ScraperConfig config) : IScraperConfigStorage
    {
        public Task CreateConfigAsync(ScraperConfig c) => Task.CompletedTask;
        public Task<ScraperConfig> GetConfigAsync() => Task.FromResult(config);
    }

    private static ScraperConfig Config(Schema? schema, int limit = int.MaxValue) => new(
        ParsingScheme: schema,
        LinkPathSelectors: ImmutableQueue<LinkPathSelector>.Empty,
        StartUrls: new[] { "https://x.test/" },
        UrlBlackList: Array.Empty<string>(),
        PageCrawlLimit: limit);

    private static Core.Spider.Concrete.Spider Spider(string html, ScraperConfig config) =>
        new(new CrawlStep(new LinkParserByCssSelector(), new AngleSharpContentParser(NullLogger.Instance)),
            new FakeLoader(html),
            new FakeConfig(config));

    private static Job Job(string url, params LinkPathSelector[] chain) =>
        new(url, ImmutableQueue.CreateRange(chain), ImmutableQueue.Create<string>());

    [Fact]
    public async Task Target_page_reports_Parsed_with_the_loaded_document()
    {
        const string html = "<html><body><h1 class='t'>Hello</h1></body></html>";
        var schema = new Schema { new("title", "h1.t") };

        // 0 selectors => target page
        var report = await Spider(html, Config(schema)).CrawlAsync(Job("https://x.test/p/1"));

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

        var report = await Spider(html, Config(schema: null))
            .CrawlAsync(Job("https://x.test/", new LinkPathSelector("a.item"), new LinkPathSelector("a.detail")));

        var followed = Assert.IsType<CrawlOutcome.Followed>(report.Outcome);
        Assert.Equal(new[] { "https://x.test/a", "https://x.test/b" }, followed.Next.Select(j => j.Url));
        Assert.Equal(html, report.Document);
    }

    [Fact]
    public async Task Shell_never_throws_to_signal_the_crawl_limit()
    {
        // The reduced shell holds no tracker and no limit rule (ADR-0022):
        // even with PageCrawlLimit 0 it just loads, steps, and reports. This
        // pins the removal of PageCrawlLimitException-as-control-flow — the
        // defect that, run through Executor's Handle<Exception> retry, this
        // ADR exists to remove.
        const string html = "<html><body><h1 class='t'>x</h1></body></html>";

        var report = await Spider(html, Config(new Schema { new("title", "h1.t") }, limit: 0))
            .CrawlAsync(Job("https://x.test/p/1"));

        Assert.IsType<CrawlOutcome.Parsed>(report.Outcome);
    }
}
