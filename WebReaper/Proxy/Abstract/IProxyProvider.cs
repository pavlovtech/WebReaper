using System.Net;

namespace WebReaper.Proxy.Abstract;

/// <summary>
/// Supplies the <see cref="WebProxy"/> the page-load transport applies to a
/// request — the seam behind proxy rotation. When a provider is configured the
/// loaders switch to the proxy-applying path. The built-in adapter is
/// <c>ValidatedProxyProvider</c> (an <see cref="IProxySource"/> filtered by
/// <see cref="IProxyValidator"/>s).
/// </summary>
public interface IProxyProvider
{
    /// <summary>
    /// The proxy to use for the next request. Called per request, so a
    /// rotating implementation returns different proxies over time; keep it
    /// cheap (cache / refresh internally rather than fetching per call).
    /// </summary>
    Task<WebProxy> GetProxyAsync();
}
