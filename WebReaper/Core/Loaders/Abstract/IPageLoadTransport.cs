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
    Task<string> LoadAsync(PageRequest request, CancellationToken cancellationToken = default);
}
