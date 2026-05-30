namespace WebReaper.Core.Loaders.Abstract;

/// <summary>
/// One page-load mechanism behind the <see cref="IPageLoader"/> (ADR 0004):
/// HTTP or headless browser. The single home for that mechanism's client /
/// launch quirks and for how it applies the optional proxy. Two real
/// adapters — <c>HttpPageLoadTransport</c> and <c>BrowserPageLoadTransport</c>
/// — so this is a genuine seam, not indirection.
/// </summary>
public interface IPageLoadTransport
{
    /// <summary>
    /// Perform the actual fetch for <paramref name="request"/> via this
    /// mechanism (HTTP or headless browser) and return its
    /// <see cref="PageLoadResult"/> (body plus response status and headers).
    /// Applies the optional proxy and this mechanism's client / launch quirks.
    /// A completed response with any status code is returned as data (ADR-0083);
    /// only a genuine no-response failure is surfaced as a
    /// <see cref="PageLoadException"/>.
    /// </summary>
    Task<PageLoadResult> LoadAsync(PageRequest request, CancellationToken cancellationToken = default);
}
