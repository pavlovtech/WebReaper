using WebReaper.Domain.Selectors;

namespace WebReaper.Core.Loaders.Abstract;

/// <summary>
/// The page-loader's cache-aside collaborator (ADR-0041). Sits at the one
/// <see cref="IPageLoader"/> dispatcher position so the cache is uniform
/// across the HTTP and headless-browser transports (ADR-0004).
/// <para>
/// The cache key is <em>(url, page-type)</em>: a Static and a Dynamic load of
/// the same URL return materially different HTML (server-rendered shell vs.
/// JS-rendered DOM) and must not be interchangeable.
/// </para>
/// <para>
/// The implementation owns its staleness policy — <see cref="TryReadAsync"/>
/// returns <c>null</c> on miss <em>or</em> stale. The default core adapter
/// is the no-op <c>NullPageCache</c>; the firecrawl-shaped TTL adapter is
/// <c>InMemoryPageCache(TimeSpan maxAge)</c>, reached via
/// <c>ScraperEngineBuilder.WithMaxAge(maxAge)</c>.
/// </para>
/// </summary>
public interface IPageCache
{
    /// <summary>
    /// Return the cached document for <paramref name="url"/> at
    /// <paramref name="pageType"/>, or <c>null</c> if there is no entry or
    /// the entry is stale by the implementation's policy.
    /// </summary>
    Task<string?> TryReadAsync(string url, PageType pageType, CancellationToken cancellationToken);

    /// <summary>
    /// Store <paramref name="document"/> for <paramref name="url"/> at
    /// <paramref name="pageType"/>. A failure is fatal to the caller only if
    /// the implementation chooses to throw — the
    /// <see cref="WebReaper.Core.Loaders.Concrete.PageLoader"/> logs and
    /// swallows so a cache write failure never fails the Crawl.
    /// </summary>
    Task WriteAsync(string url, PageType pageType, string document, CancellationToken cancellationToken);
}
