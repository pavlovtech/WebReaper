using System;
using System.Net;
using Microsoft.Extensions.Options;
using WebReaper.Proxy.Abstract;

namespace WebReaper.Proxy.Concrete;

/// <summary>
/// Options for <see cref="PingTimeoutProxyProposalValidator"/>.
/// </summary>
public sealed class PingTimeoutValidatorOptions : IOptions<PingTimeoutValidatorOptions>
{
    /// <summary>
    /// The URL to visit to validate the proxy.
    /// </summary>
    public Uri ProbeUrl { get; set; } = new("https://www.cloudflare.com/");
    /// <summary>
    /// The maximum time to wait for a response from the probe URL.
    /// </summary>
    public TimeSpan ProbeTimeout { get; set; } = TimeSpan.FromSeconds(5);
    PingTimeoutValidatorOptions IOptions<PingTimeoutValidatorOptions>.Value => this;
}

/// <summary>
/// Validates a proxy by requesting a URL and waiting for a response.
/// </summary>
public sealed class PingTimeoutProxyProposalValidator : IProxyProposalValidator
{
    private readonly PingTimeoutValidatorOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="PingTimeoutProxyProposalValidator"/> class.
    /// </summary>
    public PingTimeoutProxyProposalValidator(IOptions<PingTimeoutValidatorOptions> options)
    {
        _options = options.Value;
    }
    /// <inheritdoc/>

    public async Task<ProxyProposalValidationResult> ValidateAsync(WebProxy proxy, CancellationToken cancellationToken = default)
    {
        using HttpMessageHandler h = new HttpClientHandler
        {
            Proxy = proxy,
            UseProxy = true
        };
        using var client = new HttpClient(h, false)
        {
            Timeout = _options.ProbeTimeout
        };
        try
        {
            var response = await client.GetAsync(_options.ProbeUrl, cancellationToken);
            response.EnsureSuccessStatusCode();
            return ProxyProposalValidationResult.Valid();
        }
        catch (AggregateException ex)
        {
            if (ex.InnerExceptions.All(ex => ex is OperationCanceledException))
            {
                return default;
            }
            return ProxyProposalValidationResult.Invalid(ex);
        }
        catch (OperationCanceledException)
        {
            return default;
        }
        catch (Exception ex)
        {
            return ProxyProposalValidationResult.Invalid(ex);
        }
    }
}
