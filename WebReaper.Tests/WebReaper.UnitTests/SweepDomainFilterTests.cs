using WebReaper.Core.Crawling.Concrete;

namespace WebReaper.UnitTests;

// ADR-0081: the on-domain boundary a Site sweep follows. Default is
// same-host-plus-www (anchored on the start host); --include-subdomains widens
// to a suffix match on the apex (a documented heuristic, not PSL-correct). The
// dot boundary on the suffix match is load-bearing: "notexample.com" must not
// count as on-domain for "example.com".
public class SweepDomainFilterTests
{
    // ---- default mode: same host, www treated as the apex ----

    [Theory]
    [InlineData("https://example.com/a")]
    [InlineData("https://example.com/deep/path?q=1")]
    [InlineData("http://example.com/a")]            // scheme-agnostic
    [InlineData("https://EXAMPLE.com/a")]           // host case-insensitive
    public void Same_host_is_on_domain(string url)
    {
        Assert.True(SweepDomainFilter.IsOnDomain(url, "example.com", includeSubdomains: false));
    }

    [Fact]
    public void Leading_www_is_equal_to_the_apex_both_directions()
    {
        Assert.True(SweepDomainFilter.IsOnDomain("https://www.example.com/a", "example.com", false));
        Assert.True(SweepDomainFilter.IsOnDomain("https://example.com/a", "www.example.com", false));
    }

    [Theory]
    [InlineData("https://other.com/a")]
    [InlineData("https://blog.example.com/a")]      // a subdomain is OFF by default
    [InlineData("https://notexample.com/a")]        // dot-boundary guard
    public void Other_hosts_are_off_domain_by_default(string url)
    {
        Assert.False(SweepDomainFilter.IsOnDomain(url, "example.com", includeSubdomains: false));
    }

    // ---- include-subdomains: suffix match on the apex ----

    [Theory]
    [InlineData("https://blog.example.com/a")]
    [InlineData("https://deep.nested.example.com/a")]
    [InlineData("https://example.com/a")]           // the apex itself
    [InlineData("https://www.example.com/a")]
    public void Subdomains_are_on_domain_when_opted_in(string url)
    {
        Assert.True(SweepDomainFilter.IsOnDomain(url, "example.com", includeSubdomains: true));
    }

    [Theory]
    [InlineData("https://notexample.com/a")]        // shares no dot boundary
    [InlineData("https://example.com.evil.com/a")]  // suffix attack: not a real subdomain
    [InlineData("https://other.com/a")]
    public void Subdomain_mode_still_rejects_unrelated_hosts(string url)
    {
        Assert.False(SweepDomainFilter.IsOnDomain(url, "example.com", includeSubdomains: true));
    }

    // ---- malformed input is skipped, never thrown ----

    [Theory]
    [InlineData("/relative/path")]
    [InlineData("not a url")]
    [InlineData("")]
    public void A_non_absolute_url_is_not_on_domain(string url)
    {
        Assert.False(SweepDomainFilter.IsOnDomain(url, "example.com", includeSubdomains: false));
        Assert.False(SweepDomainFilter.IsOnDomain(url, "example.com", includeSubdomains: true));
    }
}
