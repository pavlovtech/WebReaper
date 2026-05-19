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
    /// mechanism (HTTP or headless browser) and return the page body. Applies
    /// the optional proxy and this mechanism's client / launch quirks. A page
    /// that cannot be retrieved is surfaced as an exception, not an empty
    /// string.
    /// </summary>
    Task<string> LoadAsync(PageRequest request, CancellationToken cancellationToken = default);
}
