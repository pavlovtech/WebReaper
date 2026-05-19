namespace WebReaper.Domain;

/// <summary>
/// The context handed to a <c>PostProcess</c> callback alongside the scraped
/// JSON: where the data came from and the raw page it came from, so the
/// callback can enrich or filter using the page itself and its crawl ancestry.
/// </summary>
/// <param name="BackLinks">Ancestor URLs that led to this page, oldest
/// first.</param>
/// <param name="Url">The page the data was scraped from.</param>
/// <param name="Html">The raw page body as loaded.</param>
public record Metadata(List<string> BackLinks, string Url, string Html);
