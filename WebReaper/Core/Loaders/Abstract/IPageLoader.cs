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
    /// Fetch the page for <paramref name="request"/> and return its body,
    /// dispatching on <see cref="PageRequest.PageType"/> to the matching
    /// <see cref="IPageLoadTransport"/>. A page that cannot be retrieved is
    /// surfaced as an exception (the transport decides the type), not an empty
    /// string.
    /// </summary>
    Task<string> LoadAsync(PageRequest request, CancellationToken cancellationToken = default);
}
