namespace WebReaper.Core.Parser.Abstract;

/// <summary>
/// The link-discovery half of crawling one page: extract the follow /
/// paginate URLs a selector points at. (The content half — turning a target
/// page into data — is the Schema fold, <see cref="IJsonContentParser"/> /
/// ADR-0002.) The Crawl step turns the returned URLs into candidate child
/// Jobs; visited-link filtering is the Crawl driver's job, not this seam's.
/// </summary>
public interface ILinkParser
{
    /// <summary>
    /// The absolute URLs in <paramref name="html"/> matching
    /// <paramref name="selector"/>, with relative hrefs resolved against
    /// <paramref name="baseUrl"/>. Returns the raw discovered set — not
    /// deduplicated and not visited-filtered (the driver owns that).
    /// </summary>
    Task<List<string>> GetLinksAsync(Uri baseUrl, string html, string selector);
}
