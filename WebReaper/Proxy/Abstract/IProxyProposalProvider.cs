using System;
using System.Net;

namespace WebReaper.Proxy.Abstract;

/// <summary>
/// Supplies a list of unvalidated proxies.
/// </summary>
public interface IProxyProposalProvider
{
    /// <summary>
    /// Returns a list of potential proxies, which may or may not be valid.
    /// </summary>
    Task<IEnumerable<WebProxy>> GetProxiesAsync(CancellationToken cancellationToken = default);
}
