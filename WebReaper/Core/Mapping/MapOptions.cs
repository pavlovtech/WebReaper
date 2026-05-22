namespace WebReaper.Core.Mapping;

/// <summary>
/// The knobs <see cref="ISiteMapper.MapAsync"/> accepts (ADR-0042).
/// </summary>
/// <param name="MaxUrls">Hard cap on the returned list; the union is
/// truncated to this many. Default 1000 — comfortable for the agent /
/// CLI use cases without materialising 10k-URL sitemaps.</param>
/// <param name="IncludeSitemap">Read <c>robots.txt</c> for <c>Sitemap:</c>
/// directives, then <c>sitemap.xml</c>, and emit those URLs. Default true.</param>
/// <param name="IncludeRootPageLinks">GET the root URL and emit
/// <c>&lt;a href&gt;</c> URLs (resolved absolute). Default true.</param>
/// <param name="AllowOffsite">Keep URLs whose host differs from
/// <paramref name="url"/>'s. Default false — typical root pages link
/// outbound to social/ads/CDNs which agents rarely want in the discovery
/// result.</param>
/// <param name="Search">Case-insensitive substring filter on the URLs
/// (firecrawl-shaped ranking proxy). Default <c>null</c> — no filter. Real
/// embedding-similarity ranking is out of scope for v1 (ADR-0042).</param>
public sealed record MapOptions(
    int MaxUrls = 1000,
    bool IncludeSitemap = true,
    bool IncludeRootPageLinks = true,
    bool AllowOffsite = false,
    string? Search = null);
