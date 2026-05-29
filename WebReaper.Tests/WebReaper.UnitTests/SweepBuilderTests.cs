using WebReaper.Builders;
using WebReaper.ConfigStorage.Abstract;
using WebReaper.Core.Mapping;
using WebReaper.Domain;

namespace WebReaper.UnitTests;

// ADR-0081: the .Sweep(options?) builder surface: it appends the recursive
// selector and maps the options onto the ScraperConfig, and BuildAsync seeds
// the frontier from the Site mapper when SweepOptions.Sitemap is on.
public class SweepBuilderTests
{
    private sealed class CapturingConfigStorage : IScraperConfigStorage
    {
        public ScraperConfig? Created;
        public Task CreateConfigAsync(ScraperConfig config) { Created = config; return Task.CompletedTask; }
        public Task<ScraperConfig> GetConfigAsync() => Task.FromResult(Created!);
    }

    private sealed class FakeSiteMapper(params string[] urls) : ISiteMapper
    {
        public Task<IReadOnlyList<string>> MapAsync(
            string url, MapOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(urls);
    }

    private sealed class ThrowingSiteMapper : ISiteMapper
    {
        public Task<IReadOnlyList<string>> MapAsync(
            string url, MapOptions? options = null, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("the site mapper must not be called when Sitemap is off");
    }

    [Fact]
    public void Sweep_appends_a_recursive_selector_and_maps_options_onto_the_config()
    {
        var config = ScraperEngineBuilder
            .Crawl("https://example.com/")
            .AsMarkdown()
            .Sweep(new SweepOptions { LinkSelector = "a.x", IncludeSubdomains = true, MaxDepth = 5 })
            .Build();

        var selector = Assert.Single(config.LinkPathSelectors);
        Assert.True(selector.Recursive);
        Assert.Equal("a.x", selector.Selector);
        Assert.True(config.IncludeSubdomains);
        Assert.Equal(5, config.MaxDepth);
    }

    [Fact]
    public void Sweep_defaults_to_a_href_no_subdomains_unbounded_depth()
    {
        var config = ScraperEngineBuilder
            .Crawl("https://example.com/")
            .AsMarkdown()
            .Sweep()
            .Build();

        var selector = Assert.Single(config.LinkPathSelectors);
        Assert.Equal("a[href]", selector.Selector);
        Assert.True(selector.Recursive);
        Assert.False(config.IncludeSubdomains);
        Assert.Equal(int.MaxValue, config.MaxDepth);
    }

    [Fact]
    public async Task Sweep_with_sitemap_on_seeds_the_frontier_from_discovered_urls()
    {
        var capture = new CapturingConfigStorage();

        await using var engine = await ScraperEngineBuilder
            .Crawl("https://example.com/")
            .AsMarkdown()
            .Sweep(new SweepOptions { Sitemap = true })
            .WithConfigStorage(capture)
            .WithSiteMapper(new FakeSiteMapper("https://example.com/a", "https://example.com/b"))
            .BuildAsync();

        var starts = capture.Created!.StartUrls.ToList();
        Assert.Equal(
            new[] { "https://example.com/", "https://example.com/a", "https://example.com/b" },
            starts);
    }

    [Fact]
    public async Task Sweep_with_sitemap_off_does_not_call_the_site_mapper()
    {
        var capture = new CapturingConfigStorage();

        await using var engine = await ScraperEngineBuilder
            .Crawl("https://example.com/")
            .AsMarkdown()
            .Sweep(new SweepOptions { Sitemap = false })
            .WithConfigStorage(capture)
            .WithSiteMapper(new ThrowingSiteMapper())   // throws if seeding runs
            .BuildAsync();

        Assert.Equal(new[] { "https://example.com/" }, capture.Created!.StartUrls);
    }
}
