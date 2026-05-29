using WebReaper.Cli;
using WebReaper.Cli.Commands;

namespace WebReaper.Cli.Tests;

/// <summary>
/// ADR-0081. The CrawlCommand's flag-parsing contract:
/// --schema / --output / --max-pages / --max-depth / --include-subdomains /
/// --no-sitemap, and the defaults (max-pages 1000, sitemap on, no subdomains,
/// unbounded depth).
/// </summary>
public class CrawlContextTests
{
    private static CrawlCommand.CrawlContext Parse(params string[] argv)
        => CrawlCommand.ParseContext(Args.Parse(argv));

    [Fact]
    public void Defaults_are_sitemap_on_1000_pages_no_subdomains_unbounded_depth()
    {
        var ctx = Parse("crawl", "https://example.com");

        Assert.Equal("https://example.com", ctx.Url);
        Assert.Null(ctx.SchemaPath);
        Assert.Null(ctx.Output);
        Assert.Equal(1000, ctx.MaxPages);
        Assert.Null(ctx.MaxDepth);             // unbounded
        Assert.False(ctx.IncludeSubdomains);
        Assert.True(ctx.Sitemap);
    }

    [Fact]
    public void Bare_host_url_defaults_to_https()
    {
        Assert.Equal("https://example.com", Parse("crawl", "example.com").Url);
    }

    [Fact]
    public void Max_pages_and_max_depth_flags_parse()
    {
        var ctx = Parse("crawl", "https://example.com", "--max-pages", "50", "--max-depth", "3");
        Assert.Equal(50, ctx.MaxPages);
        Assert.Equal(3, ctx.MaxDepth);
    }

    [Fact]
    public void Include_subdomains_flag_sets_the_boundary()
    {
        Assert.True(Parse("crawl", "https://example.com", "--include-subdomains").IncludeSubdomains);
    }

    [Fact]
    public void No_sitemap_flag_turns_seeding_off()
    {
        Assert.False(Parse("crawl", "https://example.com", "--no-sitemap").Sitemap);
    }

    [Fact]
    public void Schema_and_output_flags_parse()
    {
        var ctx = Parse("crawl", "https://example.com", "--schema", "s.json", "--output", "out.jsonl");
        Assert.Equal("s.json", ctx.SchemaPath);
        Assert.Equal("out.jsonl", ctx.Output);
    }
}
