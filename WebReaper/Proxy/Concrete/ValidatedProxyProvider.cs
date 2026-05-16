using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WebReaper.Proxy.Abstract;

namespace WebReaper.Proxy.Concrete;

/// <summary>
/// An <see cref="IProxyProvider"/> that only ever hands out proxies that
/// passed validation. It pulls candidates from an <see cref="IProxySource"/>,
/// keeps the ones every <see cref="IProxyValidator"/> approves, caches
/// that set, and re-validates lazily once it goes stale.
///
/// Being an <see cref="IProxyProvider"/>, it drops into the existing
/// pipeline (<c>SpiderBuilder.WithProxies</c> → page requesters / Puppeteer
/// loader) with no other changes. No background service, no extra
/// hosting dependency: refresh happens on demand, single-flighted.
/// </summary>
public sealed class ValidatedProxyProvider : IProxyProvider, IDisposable
{
    private readonly IProxySource _source;
    private readonly IReadOnlyList<IProxyValidator> _validators;
    private readonly ValidatedProxyProviderOptions _options;
    private readonly ILogger _logger;

    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private volatile WebProxy[] _validated = Array.Empty<WebProxy>();
    private DateTimeOffset _lastRefresh = DateTimeOffset.MinValue;

    public ValidatedProxyProvider(
        IProxySource source,
        IEnumerable<IProxyValidator> validators,
        ValidatedProxyProviderOptions? options = null,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(validators);

        _source = source;
        _validators = validators.ToArray();
        _options = options ?? new ValidatedProxyProviderOptions();
        _logger = logger ?? NullLogger.Instance;
    }

    public async Task<WebProxy> GetProxyAsync()
    {
        var current = _validated;

        if (current.Length == 0 || IsStale())
        {
            current = await RefreshAsync().ConfigureAwait(false);
        }

        if (current.Length == 0)
        {
            throw new InvalidOperationException(
                "ValidatedProxyProvider: no proxy passed validation. " +
                "Check the proxy source and the validators' test target.");
        }

        // Random.Shared is thread-safe; safe under the sync-over-async
        // call sites in the page requesters.
        return current[Random.Shared.Next(current.Length)];
    }

    private bool IsStale() => DateTimeOffset.UtcNow - _lastRefresh >= _options.RefreshInterval;

    private async Task<WebProxy[]> RefreshAsync()
    {
        await _refreshLock.WaitAsync().ConfigureAwait(false);
        try
        {
            // Another caller may have refreshed while we waited on the lock.
            if (_validated.Length > 0 && !IsStale())
            {
                return _validated;
            }

            IReadOnlyList<WebProxy> candidates;
            try
            {
                candidates = await _source.GetCandidatesAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (_validated.Length > 0)
            {
                // Source hiccup but we still have a usable list — keep serving it.
                _logger.LogWarning(ex, "Proxy source failed; reusing last validated set ({Count})", _validated.Length);
                return _validated;
            }

            var valid = await ValidateAllAsync(candidates).ConfigureAwait(false);

            _logger.LogInformation(
                "Proxy validation: {Valid}/{Total} candidates passed", valid.Length, candidates.Count);

            if (valid.Length == 0 && _validated.Length > 0)
            {
                // Don't wipe a working set because one refresh found nothing.
                _logger.LogWarning("No proxy passed validation this cycle; reusing previous set");
                _lastRefresh = DateTimeOffset.UtcNow;
                return _validated;
            }

            _validated = valid;
            _lastRefresh = DateTimeOffset.UtcNow;
            return valid;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<WebProxy[]> ValidateAllAsync(IReadOnlyList<WebProxy> candidates)
    {
        using var gate = new SemaphoreSlim(_options.MaxConcurrentValidations);

        var checks = candidates.Select(async proxy =>
        {
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                return (proxy, ok: await IsProxyValidAsync(proxy).ConfigureAwait(false));
            }
            finally
            {
                gate.Release();
            }
        });

        var results = await Task.WhenAll(checks).ConfigureAwait(false);
        return results.Where(r => r.ok).Select(r => r.proxy).ToArray();
    }

    private async Task<bool> IsProxyValidAsync(WebProxy proxy)
    {
        // A proxy is kept only if every validator approves it.
        foreach (var validator in _validators)
        {
            using var timeout = new CancellationTokenSource(_options.ValidationTimeout);
            try
            {
                if (!await validator.IsValidAsync(proxy, timeout.Token).ConfigureAwait(false))
                {
                    return false;
                }
            }
            catch (OperationCanceledException)
            {
                // Validator exceeded ValidationTimeout → unusable.
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Validator {Validator} threw for {Proxy}; treating as invalid",
                    validator.GetType().Name, proxy.Address);
                return false;
            }
        }

        return true;
    }

    public void Dispose() => _refreshLock.Dispose();
}
