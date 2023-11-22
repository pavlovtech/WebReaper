using System;
using System.Net;
using WebReaper.Extensions;
using WebReaper.Proxy.Abstract;

namespace WebReaper.Proxy.Concrete;

/// <summary>
/// Provides a random validated proxy.
/// </summary>
/// <seealso cref="ProxyProposalValidatorService"/>
public sealed class ValidatedProxyProvider : IProxyProvider
{
    private readonly IValidatedProxyListProvider _validatedProxySource;

    /// <summary>
    /// Initializes a new instance of the <see cref="ValidatedProxyProvider"/> class.
    /// </summary>
    public ValidatedProxyProvider(IValidatedProxyListProvider validatedProxySource)
    {
        _validatedProxySource = validatedProxySource;
    }

    /// <inheritdoc/>
    public async Task<WebProxy> GetProxyAsync(CancellationToken cancellationToken = default)
    {
        var proxies = await _validatedProxySource.GetProxiesAsync(cancellationToken);
        return proxies.ChooseRandom();
    }

    /// <inheritdoc/>
    public Task<WebProxy> GetProxyAsync()
    {
        return GetProxyAsync(default);
    }
}
