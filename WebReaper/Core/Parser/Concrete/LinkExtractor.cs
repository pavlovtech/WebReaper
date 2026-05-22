using AngleSharp;

namespace WebReaper.Core.Parser.Concrete;

/// <summary>
/// Link discovery — the follow / paginate half of crawling a page: the
/// absolute URLs a CSS selector's elements point at, relative hrefs resolved
/// against the page's base URL. The content half — turning a target page into
/// data — is the Schema fold
/// (<see cref="WebReaper.Core.Parser.Abstract.IContentExtractor"/>, ADR-0002);
/// the two stay separate concerns.
///
/// A concrete static function, deliberately not a seam (ADR-0036). There is
/// one way to discover links — query the selector's elements, resolve their
/// hrefs — and no second shape ever varied behind the former
/// <c>ILinkParser</c> interface: one adapter, indirection without variation.
/// If a genuinely different link-discovery shape ever arrives, the interface
/// is extracted then, from two real implementations.
/// </summary>
internal static class LinkExtractor
{
    /// <summary>
    /// The absolute URLs in <paramref name="html"/> matching
    /// <paramref name="cssSelector"/>, with relative hrefs resolved against
    /// <paramref name="baseUrl"/>. An element that matches the selector but
    /// carries no usable <c>href</c> — absent, empty, or whitespace — is
    /// skipped rather than crashing the step. Exact-duplicate URLs within the
    /// page are collapsed; cross-page dedup and visited-link filtering are the
    /// Crawl driver's job, not this function's.
    /// </summary>
    public static async Task<List<string>> GetLinksAsync(Uri baseUrl, string html, string cssSelector)
    {
        var config = Configuration.Default.WithDefaultLoader();
        var context = BrowsingContext.New(config);
        using var doc = await context.OpenAsync(req => req.Content(html));

        return doc
            .QuerySelectorAll(cssSelector)
            .Select(e => e.Attributes["href"]?.Value)
            .Where(href => !string.IsNullOrWhiteSpace(href))
            .Select(href => new Uri(baseUrl, href!).ToString())
            .Distinct()
            .ToList();
    }
}
