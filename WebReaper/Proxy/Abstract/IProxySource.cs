using System.Net;

namespace WebReaper.Proxy.Abstract;

/// <summary>
/// Produces the full set of candidate proxies (before validation).
/// Implemented by anything that knows where proxies come from: a paid
/// proxy API, a static list, a scraped free-proxy page, etc.
/// </summary>
public interface IProxySource
{
    /// <summary>
    /// Returns the current list of candidate proxies. Called repeatedly
    /// by <see cref="WebReaper.Proxy.Concrete.ValidatedProxyProvider"/>
    /// on every refresh, so implementations should be cheap or cache.
    /// </summary>
    Task<IReadOnlyList<WebProxy>> GetCandidatesAsync(CancellationToken cancellationToken = default);
}
