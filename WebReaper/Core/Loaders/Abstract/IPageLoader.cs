namespace WebReaper.Core.Loaders.Abstract;

/// <summary>
/// The one page-loading seam (ADR 0004). Turns a <see cref="PageRequest"/>
/// into the page's HTML, dispatching on <see cref="PageRequest.PageType"/> to
/// one <see cref="IPageLoadTransport"/>. The Spider holds a single
/// <see cref="IPageLoader"/> and is loader-blind. Replaces the former
/// single-adapter <c>IStaticPageLoader</c> / <c>IBrowserPageLoader</c> pair.
/// </summary>
public interface IPageLoader
{
    /// <summary>
    /// Fetch the page for <paramref name="request"/> and return its
    /// <see cref="PageLoadResult"/> (body plus response status and headers),
    /// dispatching on <see cref="PageRequest.PageType"/> to the matching
    /// <see cref="IPageLoadTransport"/>. A completed response with any status
    /// code is returned as data (ADR-0083); only a genuine no-response failure
    /// is surfaced as a <see cref="PageLoadException"/>.
    /// </summary>
    Task<PageLoadResult> LoadAsync(PageRequest request, CancellationToken cancellationToken = default);
}
