namespace WebReaper.Core.Mapping;

/// <summary>
/// The URL-discovery seam (ADR-0042): given a site root, return its URLs
/// without running a Crawl. The default <see cref="SiteMapper"/> reads
/// <c>robots.txt</c> for <c>Sitemap:</c> directives, then parses
/// <c>sitemap.xml</c> (recursing one level into a sitemap index), then
/// extracts <c>&lt;a href&gt;</c> links from the root page, unions, and
/// filters per <see cref="MapOptions"/>. The seam exists so the CLI / MCP /
/// authenticated-site / proxy-aware variants are substitutable.
/// <para>
/// One method, separate utility — discovery is not extraction. Pairs with
/// the <c>IContentExtractor</c> seam (ADR-0039): map a site, then scrape /
/// extract / markdown the URLs of interest.
/// </para>
/// </summary>
public interface ISiteMapper
{
    /// <summary>
    /// Return the discovered URLs for <paramref name="url"/>'s site,
    /// deduplicated and ordered <em>sitemap-then-root-page-links</em>.
    /// </summary>
    Task<IReadOnlyList<string>> MapAsync(
        string url,
        MapOptions? options = null,
        CancellationToken cancellationToken = default);
}
