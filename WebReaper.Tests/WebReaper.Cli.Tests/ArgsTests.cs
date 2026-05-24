using WebReaper.Cli;

namespace WebReaper.Cli.Tests;

// ADR-0043: the hand-rolled CLI parser. These tests pin the small grammar
// — positional args, --flag value, --flag=value, boolean flags, the
// --help / --version aliases, and the typed flag accessors.
public class ArgsTests
{
    [Fact]
    public void Parse_one_positional_one_flag_value()
    {
        var p = Args.Parse(new[] { "scrape", "https://x.test", "--schema", "s.json" });

        Assert.Equal("scrape", p.Command);
        Assert.Equal(new[] { "https://x.test" }, p.Positional);
        Assert.Equal("s.json", p.GetFlag("schema"));
    }

    [Fact]
    public void Parse_flag_equals_value()
    {
        var p = Args.Parse(new[] { "scrape", "https://x.test", "--schema=s.json" });

        Assert.Equal("s.json", p.GetFlag("schema"));
    }

    [Fact]
    public void Parse_boolean_flag()
    {
        var p = Args.Parse(new[] { "scrape", "https://x.test", "--browser" });

        Assert.True(p.HasFlag("browser"));
        Assert.Equal("true", p.GetFlag("browser"));
    }

    [Fact]
    public void Parse_dash_h_aliases_to_help_command()
    {
        var p = Args.Parse(new[] { "-h" });
        Assert.Equal("help", p.Command);

        var p2 = Args.Parse(new[] { "--help" });
        Assert.Equal("help", p2.Command);
    }

    [Fact]
    public void Parse_dash_v_aliases_to_version_command()
    {
        var p = Args.Parse(new[] { "-v" });
        Assert.Equal("version", p.Command);

        var p2 = Args.Parse(new[] { "--version" });
        Assert.Equal("version", p2.Command);
    }

    [Fact]
    public void Parse_empty_args_throws_cli_exception()
    {
        Assert.Throws<CliException>(() => Args.Parse(Array.Empty<string>()));
    }

    [Fact]
    public void Require_flag_throws_when_missing()
    {
        var p = Args.Parse(new[] { "scrape", "https://x.test" });
        var ex = Assert.Throws<CliException>(() => p.RequireFlag("schema"));
        Assert.Contains("--schema", ex.Message);
    }

    [Fact]
    public void Get_int_flag_parses_decimal_with_fallback()
    {
        var p = Args.Parse(new[] { "map", "https://x.test", "--max-urls", "42" });
        Assert.Equal(42, p.GetIntFlag("max-urls", 100));

        var p2 = Args.Parse(new[] { "map", "https://x.test" });
        Assert.Equal(100, p2.GetIntFlag("max-urls", 100));
    }

    [Fact]
    public void Get_int_flag_throws_for_non_integer()
    {
        var p = Args.Parse(new[] { "map", "https://x.test", "--max-urls", "abc" });
        Assert.Throws<CliException>(() => p.GetIntFlag("max-urls", 0));
    }

    [Theory]
    [InlineData("30s", 30, 0, 0, 0)]
    [InlineData("5m", 0, 5, 0, 0)]
    [InlineData("2h", 0, 0, 2, 0)]
    [InlineData("1d", 0, 0, 0, 1)]
    public void Get_timespan_flag_parses_shorthand(string input, int s, int m, int h, int d)
    {
        var p = Args.Parse(new[] { "scrape", "https://x.test", "--max-age", input });
        var ts = p.GetTimeSpanFlag("max-age");
        Assert.NotNull(ts);
        Assert.Equal(new TimeSpan(d, h, m, s), ts!.Value);
    }

    [Fact]
    public void Get_timespan_flag_parses_canonical_timespan()
    {
        var p = Args.Parse(new[] { "scrape", "https://x.test", "--max-age", "00:00:30" });
        Assert.Equal(TimeSpan.FromSeconds(30), p.GetTimeSpanFlag("max-age"));
    }

    [Fact]
    public void Get_timespan_flag_throws_on_bad_unit()
    {
        var p = Args.Parse(new[] { "scrape", "https://x.test", "--max-age", "5z" });
        Assert.Throws<CliException>(() => p.GetTimeSpanFlag("max-age"));
    }

    [Fact]
    public void Get_timespan_flag_returns_null_when_absent()
    {
        var p = Args.Parse(new[] { "scrape", "https://x.test" });
        Assert.Null(p.GetTimeSpanFlag("max-age"));
    }
}
