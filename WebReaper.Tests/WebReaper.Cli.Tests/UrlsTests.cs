using WebReaper.Cli;

namespace WebReaper.Cli.Tests;

/// <summary>
/// Bare-host URL ergonomics: a user-typed `webreaper scrape alexpavlov.dev`
/// must reach the library as an absolute URI. Urls.Normalize defaults a
/// missing scheme to https:// while leaving explicit schemes untouched.
/// </summary>
public class UrlsTests
{
    [Theory]
    [InlineData("alexpavlov.dev", "https://alexpavlov.dev")]
    [InlineData("example.com/path?q=1", "https://example.com/path?q=1")]
    [InlineData("sub.example.co.uk", "https://sub.example.co.uk")]
    [InlineData("localhost:3000", "https://localhost:3000")]
    public void Bare_host_gets_https_scheme(string input, string expected)
        => Assert.Equal(expected, Urls.Normalize(input));

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("http://example.com")]
    [InlineData("http://localhost:9222")]
    public void Explicit_scheme_is_preserved(string input)
        => Assert.Equal(input, Urls.Normalize(input));

    [Fact]
    public void Whitespace_is_trimmed_before_scheme_defaulting()
        => Assert.Equal("https://example.com", Urls.Normalize("  example.com  "));

    [Fact]
    public void Protocol_relative_url_defaults_to_https_without_doubling_slashes()
        => Assert.Equal("https://example.com", Urls.Normalize("//example.com"));

    [Fact]
    public void Empty_is_left_for_downstream_validation()
        => Assert.Equal("", Urls.Normalize("   "));
}
