using System.Reflection;
using Microsoft.Extensions.Logging;
using PuppeteerExtraSharp;
using PuppeteerExtraSharp.Plugins.ExtraStealth;
using PuppeteerSharp;
using PuppeteerSharp.BrowserData;
using WebReaper.Core.Actions.Abstract;
using WebReaper.Core.Actions.Concrete;
using WebReaper.Core.CookieStorage.Abstract;
using WebReaper.Core.Loaders.Abstract;
using WebReaper.Domain.PageActions;
using WebReaper.Extensions;
using WebReaper.Proxy.Abstract;

namespace WebReaper.Puppeteer;

/// <summary>
/// Headless-browser <see cref="IPageLoadTransport"/> — the one home for the
/// Puppeteer path (ADR 0004). Replaces <c>PuppeteerPageLoader</c>,
/// <c>PuppeteerPageLoaderWithProxies</c>, and the <c>BrowserPageLoader</c>
/// base. The only genuine variation between the old pair — plain Puppeteer
/// vs PuppeteerExtra + stealth + proxy args + <c>AuthenticateAsync</c> — is
/// the single <c>_proxyProvider is not null</c> branch here. Navigation waits
/// on <see cref="WaitUntilNavigation.Networkidle2"/> (the old pair diverged
/// between that and <c>DOMContentLoaded</c> by accident; networkidle is
/// correct for the JS-rendered pages this transport exists for).
/// </summary>
public class BrowserPageLoadTransport : IPageLoadTransport
{
    private readonly ICookiesStorage _cookiesStorage;
    private readonly IProxyProvider? _proxyProvider;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _downloadSemaphore = new(1, 1);

    // ADR-0050: the per-Spider SemanticAct cache + resolve sequencing live in
    // the core SemanticActCoordinator so the lifecycle is unit-testable
    // without IPage. The transport's dispatch just delegates.
    private readonly SemanticActCoordinator _semanticActCoordinator;

    /// <summary>Construct with the per-Spider collaborators (ADR-0009 factory
    /// pattern). The <paramref name="actionResolver"/> resolves
    /// <see cref="PageAction.SemanticAct"/> arms at runtime (ADR-0050).</summary>
    public BrowserPageLoadTransport(
        ICookiesStorage cookiesStorage,
        IProxyProvider? proxyProvider,
        ILogger logger,
        IActionResolver actionResolver)
    {
        _cookiesStorage = cookiesStorage;
        _proxyProvider = proxyProvider;
        _logger = logger;
        _semanticActCoordinator = new SemanticActCoordinator(actionResolver, logger);
    }

    public async Task<string> LoadAsync(PageRequest request, CancellationToken cancellationToken = default)
    {
        using var _ = _logger.LogMethodDuration();

        var browserFetcher = new BrowserFetcher(new BrowserFetcherOptions
        {
            Path = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
        });

        await _downloadSemaphore.WaitAsync(cancellationToken);
        InstalledBrowser installedBrowser;
        try
        {
            _logger.LogInformation("Downloading browser...");
            installedBrowser = await browserFetcher.DownloadAsync();
            _logger.LogInformation("Browser is downloaded");
        }
        finally
        {
            _downloadSemaphore.Release();
        }

        var executablePath = browserFetcher.GetExecutablePath(installedBrowser.BuildId);

        await using var browser = await LaunchAsync(request.Headless, executablePath);
        await using var page = await browser.NewPageAsync();

        if (_proxyProvider is not null)
        {
            var proxy = await _proxyProvider.GetProxyAsync();
            var creds = proxy.Credentials?.GetCredential(new Uri(proxy.Address!.ToString()), string.Empty);
            await page.AuthenticateAsync(new Credentials
            {
                Username = creds?.UserName,
                Password = creds?.Password
            });
        }

        var cookies = await _cookiesStorage.GetAsync();
        if (cookies != null)
            await page.SetCookieAsync(cookies.ToPuppeteerCookies(request.Url));

        await page.GoToAsync(request.Url, WaitUntilNavigation.Networkidle2);

        if (request.PageActions != null)
        {
            for (var i = 0; i < request.PageActions.Count; i++)
            {
                var pageAction = request.PageActions[i];
                _logger.LogInformation(
                    "Performing page action {current} of {count}: {action}",
                    i, request.PageActions.Count - 1, pageAction.GetType().Name);

                await PerformAsync(page, pageAction, cancellationToken);
            }
        }

        return await page.GetContentAsync();
    }

    // ADR-0035: PageAction is a closed sum — dispatch is a switch over the
    // arms, not a PageActionType-keyed dictionary that could silently lack an
    // entry (the pre-0035 KeyNotFoundException for WaitForSelector /
    // EvaluateExpression — both reachable from PageActionBuilder). A future arm
    // with no case here is an actionable throw naming it, not a bare lookup
    // miss mid-crawl. ADR-0050 added SemanticAct.
    private async Task PerformAsync(IPage page, PageAction action, CancellationToken cancellationToken)
    {
        switch (action)
        {
            case PageAction.Click a:
                await page.ClickAsync(a.Selector);
                break;
            case PageAction.Wait a:
                await Task.Delay(a.Milliseconds, cancellationToken);
                break;
            case PageAction.ScrollToEnd:
                await page.EvaluateExpressionAsync("window.scrollTo(0, document.body.scrollHeight);");
                break;
            case PageAction.EvaluateExpression a:
                await page.EvaluateExpressionAsync(a.Expression);
                break;
            case PageAction.WaitForSelector a:
                await page.WaitForSelectorAsync(
                    a.Selector, new WaitForSelectorOptions { Timeout = a.TimeoutMs });
                break;
            case PageAction.WaitForNetworkIdle:
                await page.WaitForNetworkIdleAsync();
                break;
            case PageAction.SemanticAct a:
                // ADR-0050: cache + resolve sequencing lives in
                // SemanticActCoordinator (core, unit-testable). The transport
                // supplies the IPage-bound callbacks.
                await _semanticActCoordinator.DispatchAsync(
                    a.Intent,
                    getHtmlAsync: _ => page.GetContentAsync(),
                    dispatch: (arm, ct) => PerformAsync(page, arm, ct),
                    cancellationToken);
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(action), action.GetType().Name, "unhandled PageAction arm");
        }
    }

    private async Task<IBrowser> LaunchAsync(bool headless, string executablePath)
    {
        if (_proxyProvider is null)
        {
            _logger.LogInformation("Launching a browser");
            // Fully-qualified: the satellite namespace WebReaper.Puppeteer
            // shadows the PuppeteerSharp.Puppeteer static class.
            return await PuppeteerSharp.Puppeteer.LaunchAsync(new LaunchOptions
            {
                Headless = headless,
                ExecutablePath = executablePath,
                Args = new[] { "--ignore-certificate-errors" }
            });
        }

        var proxy = await _proxyProvider.GetProxyAsync();
        var proxyAddress = $"--proxy-server={proxy.Address!.Host}:{proxy.Address.Port}";

        _logger.LogInformation("Launching a browser with stealth + proxy");
        var puppeteerExtra = new PuppeteerExtra().Use(new StealthPlugin());

        return await puppeteerExtra.LaunchAsync(new LaunchOptions
        {
            Headless = headless,
            ExecutablePath = executablePath,
            Args = new[]
            {
                "--disable-dev-shm-usage",
                "--no-sandbox",
                "--disable-setuid-sandbox",
                proxyAddress
            }
        });
    }
}
