using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using WebReaper.Core.Actions.Abstract;
using WebReaper.Core.Actions.Concrete;
using WebReaper.Core.CookieStorage.Abstract;
using WebReaper.Core.Loaders.Abstract;
using WebReaper.Domain.PageActions;
using WebReaper.Proxy.Abstract;

// Disambiguate against WebReaper.Proxy (namespace) and System.Net.Cookie (clashes with Microsoft.Playwright.Cookie).
using NetCookie = System.Net.Cookie;
using PwProxy = Microsoft.Playwright.Proxy;

namespace WebReaper.Playwright;

/// <summary>
/// Microsoft.Playwright-backed <see cref="IPageLoadTransport"/> (ADR-0053).
/// Replaces the deleted Puppeteer transport (<c>BrowserPageLoadTransport</c>).
/// Multi-browser via the <see cref="PlaywrightBrowser"/> enum; all seven
/// <see cref="PageAction"/> arms (ADR-0035) including <c>SemanticAct</c>
/// (ADR-0050).
/// </summary>
public sealed class PlaywrightPageLoadTransport : IPageLoadTransport, IAsyncDisposable
{
    private readonly ICookiesStorage _cookiesStorage;
    private readonly IProxyProvider? _proxyProvider;
    private readonly ILogger _logger;
    private readonly SemanticActCoordinator _semanticActCoordinator;
    private readonly PlaywrightBrowser _browserKind;
    private readonly PlaywrightLaunchOptions _launchOptions;

    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _disposed;

    /// <summary>Construct with the per-Spider collaborators (ADR-0009 factory
    /// pattern). The <paramref name="actionResolver"/> resolves
    /// <see cref="PageAction.SemanticAct"/> arms at runtime (ADR-0050).</summary>
    public PlaywrightPageLoadTransport(
        PlaywrightBrowser browserKind,
        PlaywrightLaunchOptions launchOptions,
        ICookiesStorage cookiesStorage,
        IProxyProvider? proxyProvider,
        ILogger logger,
        IActionResolver actionResolver)
    {
        _browserKind = browserKind;
        _launchOptions = launchOptions;
        _cookiesStorage = cookiesStorage;
        _proxyProvider = proxyProvider;
        _logger = logger;
        _semanticActCoordinator = new SemanticActCoordinator(actionResolver, logger);
    }

    /// <inheritdoc />
    public async Task<string> LoadAsync(PageRequest request, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var browser = await EnsureLaunchedAsync(request.Headless, cancellationToken);

        var contextOptions = new BrowserNewContextOptions();
        if (_proxyProvider is not null)
        {
            var proxy = await _proxyProvider.GetProxyAsync();
            if (proxy.Address is not null)
            {
                contextOptions.Proxy = new PwProxy
                {
                    Server = $"{proxy.Address.Scheme}://{proxy.Address.Host}:{proxy.Address.Port}",
                };
                var creds = proxy.Credentials?.GetCredential(proxy.Address, string.Empty);
                if (creds is not null)
                {
                    contextOptions.Proxy.Username = creds.UserName;
                    contextOptions.Proxy.Password = creds.Password;
                }
            }
        }

        await using var context = await browser.NewContextAsync(contextOptions);
        await ApplyCookiesAsync(context, request.Url);

        var page = await context.NewPageAsync();
        await page.GotoAsync(request.Url, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });

        if (request.PageActions is { Count: > 0 })
        {
            for (var i = 0; i < request.PageActions.Count; i++)
            {
                var action = request.PageActions[i];
                _logger.LogInformation(
                    "Performing page action {Current} of {Count}: {Action}",
                    i, request.PageActions.Count - 1, action.GetType().Name);
                await PerformAsync(page, action, cancellationToken);
            }
        }

        return await page.ContentAsync();
    }

    // ADR-0035 closed-sum dispatch. All seven arms implemented — Puppeteer's
    // four-arm coverage that threw at runtime on WaitForSelector and
    // EvaluateExpression is closed here.
    private async Task PerformAsync(IPage page, PageAction action, CancellationToken ct)
    {
        switch (action)
        {
            case PageAction.Click a:
                await page.ClickAsync(a.Selector);
                break;
            case PageAction.Wait a:
                await Task.Delay(a.Milliseconds, ct);
                break;
            case PageAction.ScrollToEnd:
                await page.EvaluateAsync("window.scrollTo(0, document.body.scrollHeight)");
                break;
            case PageAction.EvaluateExpression a:
                await page.EvaluateAsync(a.Expression);
                break;
            case PageAction.WaitForSelector a:
                await page.WaitForSelectorAsync(a.Selector, new PageWaitForSelectorOptions
                {
                    Timeout = a.TimeoutMs,
                });
                break;
            case PageAction.WaitForNetworkIdle:
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                break;
            case PageAction.ScrollIntoView a:
                // ADR-0074: Playwright's ScrollIntoViewIfNeededAsync natively
                // auto-waits for the element and scrolls it into the viewport.
                await page.Locator(a.Selector).ScrollIntoViewIfNeededAsync();
                break;
            case PageAction.SemanticAct a:
                await _semanticActCoordinator.DispatchAsync(
                    a.Intent,
                    getHtmlAsync: _ => page.ContentAsync(),
                    dispatch: (arm, token) => PerformAsync(page, arm, token),
                    ct);
                break;
            case PageAction.Press a:
                // ADR-0074: Playwright accepts the same key-string format natively.
                await page.Keyboard.PressAsync(a.Key);
                break;
            case PageAction.Fill a:
                // ADR-0074: one line; native auto-wait + clear + framework events
                // handled by Playwright's page.FillAsync.
                await page.FillAsync(a.Selector, a.Value);
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(action), action.GetType().Name, "unhandled PageAction arm");
        }
    }

    private async Task ApplyCookiesAsync(IBrowserContext context, string url)
    {
        var container = await _cookiesStorage.GetAsync();
        if (container is null) return;
        var cookies = container.GetAllCookies();
        if (cookies.Count == 0) return;

        var playwrightCookies = new List<global::Microsoft.Playwright.Cookie>(cookies.Count);
        foreach (NetCookie c in cookies)
        {
            var pwc = new global::Microsoft.Playwright.Cookie
            {
                Name = c.Name,
                Value = c.Value,
                Path = string.IsNullOrEmpty(c.Path) ? "/" : c.Path,
                Secure = c.Secure,
                HttpOnly = c.HttpOnly,
            };
            if (!string.IsNullOrWhiteSpace(c.Domain)) pwc.Domain = c.Domain;
            else pwc.Url = url;
            if (c.Expires != DateTime.MinValue)
                pwc.Expires = new DateTimeOffset(c.Expires.ToUniversalTime()).ToUnixTimeSeconds();
            playwrightCookies.Add(pwc);
        }

        await context.AddCookiesAsync(playwrightCookies);
    }

    private async Task<IBrowser> EnsureLaunchedAsync(bool headless, CancellationToken ct)
    {
        if (_browser is not null) return _browser;
        await _initLock.WaitAsync(ct);
        try
        {
            if (_browser is not null) return _browser;
            _playwright ??= await global::Microsoft.Playwright.Playwright.CreateAsync();

            var browserType = _browserKind switch
            {
                PlaywrightBrowser.Firefox => _playwright.Firefox,
                PlaywrightBrowser.Webkit => _playwright.Webkit,
                _ => _playwright.Chromium,
            };

            var launchOpts = new BrowserTypeLaunchOptions
            {
                Headless = headless && _launchOptions.Headless,
                Channel = _launchOptions.Channel,
                ExecutablePath = _launchOptions.ExecutablePath,
                Args = _launchOptions.Args,
            };
            _launchOptions.ConfigureLaunchOptions?.Invoke(launchOpts);

            _browser = await browserType.LaunchAsync(launchOpts);
            return _browser;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        if (_browser is not null) await _browser.DisposeAsync();
        _playwright?.Dispose();
        _initLock.Dispose();
    }
}
