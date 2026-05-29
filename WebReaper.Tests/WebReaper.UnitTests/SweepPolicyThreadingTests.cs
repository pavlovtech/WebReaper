using System.Collections.Immutable;
using WebReaper.Builders;
using WebReaper.Core.Crawling;
using WebReaper.Core.Loaders.Abstract;
using WebReaper.Core.Spider.Abstract;
using WebReaper.Domain;
using WebReaper.Domain.Parsing;
using WebReaper.Domain.Selectors;

namespace WebReaper.UnitTests;

// ADR-0081: SpiderBuilder derives the SweepPolicy from the ScraperConfig (the
// anchor host from the start URL, IncludeSubdomains + MaxDepth verbatim) and
// threads it into the CrawlStep. This pins the config → CrawlStep seam end to
// end through a real Spider (a fake loader, no network), so a Sweep built
// through the config path (the engine driver OR a distributed worker, both of
// which go through SpiderBuilder.Build) filters children by the configured
// boundary.
public class SweepPolicyThreadingTests
{
    private sealed class FakeLoader(string html) : IPageLoader
    {
        public Task<string> LoadAsync(PageRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(html);
    }

    private static ScraperConfig SweepConfig(bool includeSubdomains, int maxDepth = int.MaxValue) => new(
        ParsingScheme: new Schema { new("title", "h1") },
        LinkPathSelectors: ImmutableQueue.CreateRange(new[] { LinkPathSelector.Sweep() }),
        StartUrls: new[] { "https://example.com/" },
        UrlBlackList: Array.Empty<string>(),
        IncludeSubdomains: includeSubdomains,
        MaxDepth: maxDepth);

    private static ISpider BuildSweepSpider(ScraperConfig config, string html) =>
        new SpiderBuilder().WithPageLoader(new FakeLoader(html)).WithConfig(config).Build();

    private static Job StartJob() => new(
        "https://example.com/",
        ImmutableQueue.CreateRange(new[] { LinkPathSelector.Sweep() }),
        ImmutableQueue<string>.Empty);

    [Fact]
    public async Task SpiderBuilder_threads_IncludeSubdomains_from_config_into_the_sweep()
    {
        const string html = "<html><body><h1>h</h1>" +
                            "<a href='https://blog.example.com/x'>sub</a>" +
                            "<a href='https://example.com/a'>same</a></body></html>";

        var off = await BuildSweepSpider(SweepConfig(includeSubdomains: false), html).CrawlAsync(StartJob());
        var sweptOff = Assert.IsType<CrawlOutcome.Swept>(off.Outcome);
        Assert.Equal(new[] { "https://example.com/a" }, sweptOff.Next.Select(j => j.Url));

        var on = await BuildSweepSpider(SweepConfig(includeSubdomains: true), html).CrawlAsync(StartJob());
        var sweptOn = Assert.IsType<CrawlOutcome.Swept>(on.Outcome);
        Assert.Equal(new[] { "https://blog.example.com/x", "https://example.com/a" },
            sweptOn.Next.Select(j => j.Url));
    }

    [Fact]
    public async Task SpiderBuilder_threads_MaxDepth_from_config()
    {
        const string html = "<html><body><h1>h</h1><a href='https://example.com/a'>x</a></body></html>";
        var spider = BuildSweepSpider(SweepConfig(includeSubdomains: false, maxDepth: 1), html);

        // Depth 0 (the start page) follows.
        var d0 = Assert.IsType<CrawlOutcome.Swept>((await spider.CrawlAsync(StartJob())).Outcome);
        Assert.NotEmpty(d0.Next);

        // Depth 1 (one backlink) is at the cap: extract only.
        var atCap = new Job(
            "https://example.com/b",
            ImmutableQueue.CreateRange(new[] { LinkPathSelector.Sweep() }),
            ImmutableQueue.CreateRange(new[] { "https://example.com/" }));
        var d1 = Assert.IsType<CrawlOutcome.Swept>((await spider.CrawlAsync(atCap)).Outcome);
        Assert.Empty(d1.Next);
    }
}
