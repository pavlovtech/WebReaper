using System;
using System.Net;

namespace WebReaper.Proxy.Abstract;

/// <summary>
/// Supplies a list of validated, ready to use proxies.
/// </summary>
public interface IValidatedProxyListProvider
{
    /// <summary>
    /// Returns a list of validated proxies.
    /// </summary>
    Task<IEnumerable<WebProxy>> GetProxiesAsync(CancellationToken cancellationToken = default);
}
