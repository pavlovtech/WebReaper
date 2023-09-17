using System;
using System.Net;
using WebReaper.Proxy.Concrete;

namespace WebReaper.Proxy.Abstract;

/// <summary>
/// Validates a proposed proxy.
/// </summary>
public interface IProxyProposalValidator
{
    /// <summary>
    /// Validates a proposed proxy.
    /// </summary>
    /// <returns>A <see cref="ProxyProposalValidationResult"/> indicating whether the proxy is valid or invalid, or the validator does not apply to the result.</returns>
    Task<ProxyProposalValidationResult> ValidateAsync(WebProxy proxy, CancellationToken cancellationToken = default);
}
