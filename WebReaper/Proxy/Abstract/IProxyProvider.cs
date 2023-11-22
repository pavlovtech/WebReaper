using System.Net;

namespace WebReaper.Proxy.Abstract;

/// <summary>
/// Provides a validated proxy.
/// </summary>
public interface IProxyProvider
{
    /// <summary>
    /// Returns a validated proxy.
    /// </summary>
    Task<WebProxy> GetProxyAsync();
}