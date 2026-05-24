using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using WebReaper.Core.Actions.Abstract;
using WebReaper.Domain.PageActions;

namespace WebReaper.Core.Actions.Concrete;

/// <summary>
/// The per-Spider SemanticAct cache + resolve-coordinator (ADR-0050). Lifted
/// out of the Puppeteer transport so the cache lifecycle and the
/// resolve-then-dispatch sequencing live in core, unit-testable without an
/// <c>IPage</c> or Chromium. A dynamic-page transport (the Puppeteer satellite,
/// or any future browser transport) instantiates one per Spider and delegates
/// each <see cref="PageAction.SemanticAct"/> case to <see cref="DispatchAsync"/>,
/// supplying two <c>IPage</c>-bound callbacks: <c>getHtmlAsync</c> (only invoked
/// on a cache miss) and <c>dispatch</c> (invokes the satellite's per-arm
/// dispatcher on the resolved concrete arm).
/// <para>
/// The asymmetric retry semantics from ADR-0050 live here:
/// </para>
/// <list type="bullet">
///   <item><description>Cached arm dispatch throws ⇒ invalidate + re-resolve.</description></item>
///   <item><description>Resolver returns <c>null</c> ⇒ throw <see cref="SemanticActResolutionException"/>.</description></item>
///   <item><description>Freshly-resolved arm dispatch throws ⇒ surface; the Spider-level retry policy (ADR-0026) catches the whole job if appropriate.</description></item>
/// </list>
/// </summary>
public sealed class SemanticActCoordinator
{
    private readonly IActionResolver _resolver;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, PageAction> _cache = new();

    /// <summary>Construct with the per-Spider <paramref name="resolver"/> and
    /// <paramref name="logger"/>.</summary>
    /// <exception cref="ArgumentNullException">either argument is null.</exception>
    public SemanticActCoordinator(IActionResolver resolver, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(logger);
        _resolver = resolver;
        _logger = logger;
    }

    /// <summary>The count of cached intents (test seam).</summary>
    public int CacheCount => _cache.Count;

    /// <summary>The cached arm for an intent, or <c>null</c> on miss (test seam).</summary>
    public PageAction? TryGetCached(string intent)
        => _cache.TryGetValue(intent, out var v) ? v : null;

    /// <summary>
    /// Resolve <paramref name="intent"/> cache-first and call
    /// <paramref name="dispatch"/> with the resolved arm. The
    /// <paramref name="getHtmlAsync"/> callback is only invoked on a cache
    /// miss — the deterministic path doesn't re-read page HTML.
    /// </summary>
    public async Task DispatchAsync(
        string intent,
        Func<CancellationToken, Task<string>> getHtmlAsync,
        Func<PageAction, CancellationToken, Task> dispatch,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(intent);
        ArgumentNullException.ThrowIfNull(getHtmlAsync);
        ArgumentNullException.ThrowIfNull(dispatch);

        if (_cache.TryGetValue(intent, out var cached))
        {
            try
            {
                _logger.LogDebug(
                    "SemanticAct cache hit for intent '{intent}' -> {arm}",
                    intent, cached.GetType().Name);
                await dispatch(cached, cancellationToken);
                return;
            }
            catch (OperationCanceledException)
            {
                // Cancellation is not a resolution failure; keep the cache.
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Cached SemanticAct arm for intent '{intent}' failed on this page; invalidating and re-resolving.",
                    intent);
                _cache.TryRemove(intent, out _);
                // fall through to the resolver
            }
        }

        var html = await getHtmlAsync(cancellationToken);

        PageAction? resolved;
        try
        {
            resolved = await _resolver.ResolveAsync(intent, html, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new SemanticActResolutionException(intent, ex);
        }

        if (resolved is null)
            throw new SemanticActResolutionException(intent);

        // A resolver returning SemanticAct would loop the dispatcher. Surface
        // it as the typed exception — same shape as the null case.
        if (resolved is PageAction.SemanticAct)
            throw new SemanticActResolutionException(intent,
                new InvalidOperationException(
                    "IActionResolver.ResolveAsync returned a SemanticAct — resolvers must return a concrete arm."));

        // Cache only on successful dispatch. A throw below propagates and the
        // resolution is not cached.
        await dispatch(resolved, cancellationToken);
        _cache[intent] = resolved;
        _logger.LogInformation(
            "SemanticAct intent '{intent}' resolved to {arm} and cached for this crawl.",
            intent, resolved.GetType().Name);
    }
}
