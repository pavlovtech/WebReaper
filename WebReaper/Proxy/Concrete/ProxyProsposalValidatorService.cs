using System;
using System.Net;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebReaper.Extensions;
using WebReaper.Proxy.Abstract;

namespace WebReaper.Proxy.Concrete;

/// <summary>
/// Options for <see cref="ProxyProposalValidatorService"/>.
/// </summary>
public sealed class ProxyProposalValidatorServiceOptions : IOptions<ProxyProposalValidatorServiceOptions>
{
    /// <summary>
    /// The interval at which to validate proxies.
    /// </summary>
    public TimeSpan ValidationInterval { get; set; } = TimeSpan.FromMinutes(2);
    ProxyProposalValidatorServiceOptions IOptions<ProxyProposalValidatorServiceOptions>.Value => this;
}

/// <summary>
/// Periodically validates proxies and supplies a the most recently validated list of proxies.
/// </summary>
public sealed class ProxyProposalValidatorService : BackgroundService, IValidatedProxyListProvider
{
    private readonly ProxyProposalValidatorServiceOptions _options;
    private readonly ILogger<ProxyProposalValidatorService> _logger;
    private readonly IEnumerable<IProxyProposalProvider> _proxySuppliers;
    private readonly IEnumerable<IProxyProposalValidator> _proxyValidators;
    private TaskCompletionSource<IEnumerable<WebProxy>> _proxiesCompletion = new();

    /// <summary>
    /// Periodically validates proxies and supplies a the most recently validated list of proxies.
    /// </summary>
    public ProxyProposalValidatorService(
        IOptions<ProxyProposalValidatorServiceOptions> options,
        ILogger<ProxyProposalValidatorService> logger,
        IEnumerable<IProxyProposalProvider> proxySuppliers,
        IEnumerable<IProxyProposalValidator> proxyValidators
    )
    {
        _options = options.Value;
        _logger = logger;
        _proxySuppliers = proxySuppliers;
        _proxyValidators = proxyValidators;
    }

    /// <inheritdoc/>
    public Task<IEnumerable<WebProxy>> GetProxiesAsync(CancellationToken cancellationToken = default)
    {
        return _proxiesCompletion.Task.WaitAsync(cancellationToken);
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var proxies = await Task.WhenAll(_proxySuppliers.Select(supplier => supplier.GetProxiesAsync(stoppingToken)));
            var validatedProxies = await Task.WhenAll(proxies
                .SelectMany(proxy => proxy)
                .Select(proxy => FilterAvailableProxy(proxy, stoppingToken))
            );
            // update the completion source
            UpdateValidatedProxies(validatedProxies.SelectTruthy());

            await Task.Delay(_options.ValidationInterval, stoppingToken);
            stoppingToken.ThrowIfCancellationRequested();
        }
    }

    private void UpdateValidatedProxies(IEnumerable<WebProxy> validatedProxies)
    {
        // Try to set the uncompleted task
        if (!_proxiesCompletion.TrySetResult(validatedProxies))
        {
            // Replace the completed task with a new completed task
            TaskCompletionSource<IEnumerable<WebProxy>> completion = new();
            completion.SetResult(validatedProxies);
            _proxiesCompletion = completion;
        }
    }

    private async Task<WebProxy?> FilterAvailableProxy(WebProxy proxy, CancellationToken stoppingToken)
    {
        var result = await ValidateProxy(proxy, stoppingToken);
        if (result.IsInvalid)
        {
            _logger.LogWarning(result.Error, "Proxy {proxy} is invalid", proxy.Address);
            return null;
        }
        return proxy;
    }

    private async Task<ProxyProposalValidationResult> ValidateProxy(WebProxy webProxy, CancellationToken cancellationToken = default)
    {
        var results = await Task.WhenAll(_proxyValidators.Select(async validator => await validator.ValidateAsync(webProxy, cancellationToken)));
        if (results.All(x => !x.IsInvalid))
        {
            return ProxyProposalValidationResult.Valid();
        }
        AggregateException error = new("No valid proxy found", results.SelectTruthy(x => x.Error));
        return ProxyProposalValidationResult.Invalid(error);
    }
}
