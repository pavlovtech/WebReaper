using System.Net;

namespace WebReaper.Proxy.Abstract;

/// <summary>
/// Decides whether a single candidate proxy is usable. Multiple
/// validators can be combined; a proxy is kept only if every validator
/// approves it (logical AND).
/// </summary>
public interface IProxyValidator
{
    /// <summary>
    /// Returns <c>true</c> if the proxy passes this validator's check.
    /// Implementations must not throw for an unusable proxy: catch the
    /// failure and return <c>false</c>. Honor <paramref name="cancellationToken"/>.
    /// </summary>
    Task<bool> IsValidAsync(WebProxy proxy, CancellationToken cancellationToken = default);
}
