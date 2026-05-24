using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using WebReaper.Core.Actions.Abstract;
using WebReaper.Core.Actions.Concrete;
using WebReaper.Core.CookieStorage.Abstract;
using WebReaper.Core.Loaders.Abstract;
using WebReaper.Domain.PageActions;
using WebReaper.Proxy.Abstract;

namespace WebReaper.Cdp;

/// <summary>
/// Raw CDP <see cref="IPageLoadTransport"/> (ADR-0052). Connects to a CDP
/// WebSocket (either BYO via <c>WithCdpPageLoader(cdpUrl)</c> or a
/// launch-and-connect spawn via <c>WithCdpPageLoader(CdpLaunchOptions)</c>),
/// creates a fresh target per request, navigates, runs the seven
/// <see cref="PageAction"/> arms via <c>Runtime.evaluate</c> + CDP primitives,
/// and returns the rendered HTML. AOT-clean — no PuppeteerSharp,
/// no Microsoft.Playwright.
/// </summary>
public sealed class CdpPageLoadTransport : IPageLoadTransport, IAsyncDisposable
{
    private readonly ICookiesStorage _cookiesStorage;
    private readonly IProxyProvider? _proxyProvider;
    private readonly ILogger _logger;
    private readonly SemanticActCoordinator _semanticActCoordinator;
    private readonly Func<CancellationToken, Task<string>> _cdpUrlProvider;
    private readonly Func<ValueTask>? _disposeUrlProvider;

    // The "browser" WebSocket is opened lazily on the first LoadAsync and
    // reused across requests; per-request page targets are created within it.
    private CdpClient? _browserClient;
    private string? _resolvedCdpUrl;
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private bool _disposed;

    /// <summary>Construct against an already-resolved CDP URL (BYO scenario).</summary>
    public CdpPageLoadTransport(
        string cdpUrl,
        ICookiesStorage cookiesStorage,
        IProxyProvider? proxyProvider,
        ILogger logger,
        IActionResolver actionResolver)
        : this(_ => Task.FromResult(cdpUrl), null, cookiesStorage, proxyProvider, logger, actionResolver)
    {
        if (proxyProvider is not null)
            logger.LogWarning(
                "CdpPageLoadTransport: an IProxyProvider was registered with the connect-to-existing overload. " +
                "Proxy is ignored — the BYO browser owns its own proxy configuration.");
    }

    /// <summary>Construct with a CDP-URL provider (lazy launch-and-connect).
    /// The provider runs on first <see cref="LoadAsync"/>; its
    /// <paramref name="disposeUrlProvider"/> runs at transport disposal —
    /// the right place to tear down a launch-and-connect spawn.</summary>
    public CdpPageLoadTransport(
        Func<CancellationToken, Task<string>> cdpUrlProvider,
        Func<ValueTask>? disposeUrlProvider,
        ICookiesStorage cookiesStorage,
        IProxyProvider? proxyProvider,
        ILogger logger,
        IActionResolver actionResolver)
    {
        _cdpUrlProvider = cdpUrlProvider;
        _disposeUrlProvider = disposeUrlProvider;
        _cookiesStorage = cookiesStorage;
        _proxyProvider = proxyProvider;
        _logger = logger;
        _semanticActCoordinator = new SemanticActCoordinator(actionResolver, logger);
    }

    /// <inheritdoc />
    public async Task<string> LoadAsync(PageRequest request, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var browser = await EnsureConnectedAsync(cancellationToken);

        // Create a fresh page target for this request.
        var createTargetResult = await browser.SendAsync("Target.createTarget",
            new JsonObject { ["url"] = "about:blank" }, sessionId: null, cancellationToken);
        var targetId = createTargetResult["targetId"]?.GetValue<string>()
            ?? throw new CdpException("Target.createTarget returned no targetId.");

        try
        {
            // Attach with flatten: true so subsequent commands carry sessionId
            // on the same browser-level WebSocket.
            var attachResult = await browser.SendAsync("Target.attachToTarget",
                new JsonObject { ["targetId"] = targetId, ["flatten"] = true },
                sessionId: null, cancellationToken);
            var sessionId = attachResult["sessionId"]?.GetValue<string>()
                ?? throw new CdpException("Target.attachToTarget returned no sessionId.");

            await browser.SendAsync("Page.enable", null, sessionId, cancellationToken);
            await browser.SendAsync("Runtime.enable", null, sessionId, cancellationToken);
            await browser.SendAsync("Network.enable", null, sessionId, cancellationToken);

            // Apply stored cookies (best-effort; CDP rejects malformed entries
            // — we swallow per-cookie failures via a try/catch on the batch).
            await ApplyCookiesAsync(browser, sessionId, request.Url, cancellationToken);

            // Navigate. Wait for the load event with a generous timeout.
            await browser.SendAsync("Page.navigate",
                new JsonObject { ["url"] = request.Url }, sessionId, cancellationToken);
            try
            {
                await browser.WaitForEventAsync("Page.loadEventFired",
                    sessionId, TimeSpan.FromSeconds(30), cancellationToken);
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("CdpPageLoadTransport: Page.loadEventFired timed out for {Url}; continuing with current DOM.", request.Url);
            }

            // Small settle delay — analogous to Puppeteer's Networkidle2 wait.
            await Task.Delay(200, cancellationToken);

            if (request.PageActions is { Count: > 0 })
            {
                for (var i = 0; i < request.PageActions.Count; i++)
                {
                    var action = request.PageActions[i];
                    _logger.LogInformation(
                        "Performing page action {Current} of {Count}: {Action}",
                        i, request.PageActions.Count - 1, action.GetType().Name);
                    await PerformAsync(browser, sessionId, action, cancellationToken);
                }
            }

            var html = await EvaluateAsync(browser, sessionId,
                "document.documentElement.outerHTML", cancellationToken);
            return html ?? "";
        }
        finally
        {
            try
            {
                await browser.SendAsync("Target.closeTarget",
                    new JsonObject { ["targetId"] = targetId }, sessionId: null, cancellationToken);
            }
            catch { /* best-effort cleanup */ }
        }
    }

    // ADR-0035 closed-sum dispatch — switch over the seven arms.
    // Same shape as the Puppeteer transport's PerformAsync; under CDP every
    // arm reduces to one or two Runtime.evaluate / CDP primitive calls.
    private async Task PerformAsync(CdpClient browser, string sessionId, PageAction action, CancellationToken ct)
    {
        switch (action)
        {
            case PageAction.Click a:
                // Use the page's own click() to honour pointer-events/disabled.
                await EvaluateAsync(browser, sessionId,
                    $"(() => {{ const el = document.querySelector({JsonStringLiteral(a.Selector)}); if (!el) throw new Error('Selector not found: ' + {JsonStringLiteral(a.Selector)}); el.click(); }})()",
                    ct);
                break;
            case PageAction.Wait a:
                await Task.Delay(a.Milliseconds, ct);
                break;
            case PageAction.ScrollToEnd:
                await EvaluateAsync(browser, sessionId,
                    "window.scrollTo(0, document.body.scrollHeight)", ct);
                break;
            case PageAction.EvaluateExpression a:
                await EvaluateAsync(browser, sessionId, a.Expression, ct);
                break;
            case PageAction.WaitForSelector a:
                await WaitForSelectorAsync(browser, sessionId, a.Selector, a.TimeoutMs, ct);
                break;
            case PageAction.WaitForNetworkIdle:
                // v1: a simple settle wait. Full request-tracking implementation
                // is a follow-up (instrument Network.requestWillBeSent +
                // Network.loadingFinished/Failed, debounce on zero in-flight).
                await Task.Delay(500, ct);
                break;
            case PageAction.SemanticAct a:
                await _semanticActCoordinator.DispatchAsync(
                    a.Intent,
                    getHtmlAsync: token => GetHtmlAsync(browser, sessionId, token),
                    dispatch: (arm, token) => PerformAsync(browser, sessionId, arm, token),
                    ct);
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(action), action.GetType().Name, "unhandled PageAction arm");
        }
    }

    private static async Task WaitForSelectorAsync(CdpClient browser, string sessionId, string selector, int timeoutMs, CancellationToken ct)
    {
        // Poll every 50ms. Cheap; bounded by timeoutMs.
        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var found = await EvaluateAsync(browser, sessionId,
                $"!!document.querySelector({JsonStringLiteral(selector)})", ct);
            if (found == "true") return;
            await Task.Delay(50, ct);
        }
        throw new TimeoutException($"Timed out waiting {timeoutMs}ms for selector: {selector}");
    }

    private static async Task<string?> EvaluateAsync(CdpClient browser, string sessionId, string expression, CancellationToken ct)
    {
        var result = await browser.SendAsync("Runtime.evaluate",
            new JsonObject
            {
                ["expression"] = expression,
                ["awaitPromise"] = true,
                ["returnByValue"] = true,
            },
            sessionId, ct);

        if (result["exceptionDetails"] is JsonObject ex)
        {
            var text = ex["text"]?.GetValue<string>() ?? "Runtime.evaluate failed";
            throw new CdpException(text);
        }

        // result.result.value carries the boxed primitive when returnByValue.
        var value = result["result"]?["value"];
        if (value is null) return null;
        return value.GetValueKind() switch
        {
            System.Text.Json.JsonValueKind.String => value.GetValue<string>(),
            _ => value.ToJsonString(),
        };
    }

    private static Task<string> GetHtmlAsync(CdpClient browser, string sessionId, CancellationToken ct) =>
        EvaluateAsync(browser, sessionId, "document.documentElement.outerHTML", ct)!;

    private static string JsonStringLiteral(string s)
    {
        // Inline JSON-string literal for embedding selector text into a JS
        // expression. JsonNode handles the escaping correctly.
        return JsonValue.Create(s)!.ToJsonString();
    }

    private async Task ApplyCookiesAsync(CdpClient browser, string sessionId, string url, CancellationToken ct)
    {
        var container = await _cookiesStorage.GetAsync();
        if (container is null) return;

        var cookies = container.GetAllCookies();
        if (cookies.Count == 0) return;

        var arr = new JsonArray();
        foreach (Cookie c in cookies)
        {
            var entry = new JsonObject
            {
                ["name"] = c.Name,
                ["value"] = c.Value,
                ["path"] = string.IsNullOrEmpty(c.Path) ? "/" : c.Path,
                ["secure"] = c.Secure,
                ["httpOnly"] = c.HttpOnly,
            };
            if (!string.IsNullOrWhiteSpace(c.Domain)) entry["domain"] = c.Domain;
            else entry["url"] = url;
            if (c.Expires != DateTime.MinValue)
                entry["expires"] = new DateTimeOffset(c.Expires.ToUniversalTime()).ToUnixTimeSeconds();
            // Use the explicit JsonNode overload of Add — the generic
            // Add<T>(T) variant carries IL2026/IL3050 (AOT-hostile).
            arr.Add((JsonNode)entry);
        }

        try
        {
            await browser.SendAsync("Network.setCookies",
                new JsonObject { ["cookies"] = arr }, sessionId, ct);
        }
        catch (CdpException ex)
        {
            _logger.LogWarning(ex, "CdpPageLoadTransport: Network.setCookies failed; continuing without preserved cookies.");
        }
    }

    private async Task<CdpClient> EnsureConnectedAsync(CancellationToken ct)
    {
        if (_browserClient is not null) return _browserClient;
        await _connectLock.WaitAsync(ct);
        try
        {
            if (_browserClient is not null) return _browserClient;
            var url = _resolvedCdpUrl ??= await _cdpUrlProvider(ct);
            var client = new CdpClient(url);
            await client.ConnectAsync(ct);
            _browserClient = client;
            return client;
        }
        finally
        {
            _connectLock.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        if (_browserClient is not null) await _browserClient.DisposeAsync();
        if (_disposeUrlProvider is not null) await _disposeUrlProvider();
        _connectLock.Dispose();
    }
}
