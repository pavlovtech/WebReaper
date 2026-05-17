using System.Reflection;
using Microsoft.Extensions.Logging;
using PuppeteerExtraSharp;
using PuppeteerExtraSharp.Plugins.ExtraStealth;
using PuppeteerSharp;
using PuppeteerSharp.BrowserData;
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
    // Preserved verbatim from the removed BrowserPageLoader base — same four
    // handled actions (WaitForSelector / EvaluateExpression were never wired;
    // out of scope for this deepening, see ADR 0004).
    private static readonly Dictionary<PageActionType, Func<IPage, object[], Task>> PageActions = new()
    {
        {
            PageActionType.ScrollToEnd,
            async (page, _) => await page.EvaluateExpressionAsync("window.scrollTo(0, document.body.scrollHeight);")
        },
        { PageActionType.Wait, async (_, data) => await Task.Delay(Convert.ToInt32(data.First())) },
        { PageActionType.WaitForNetworkIdle, async (page, _) => await page.WaitForNetworkIdleAsync() },
        { PageActionType.Click, async (page, data) => await page.ClickAsync((string)data.First()) }
    };

    private readonly ICookiesStorage _cookiesStorage;
    private readonly IProxyProvider? _proxyProvider;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _downloadSemaphore = new(1, 1);

    public BrowserPageLoadTransport(ICookiesStorage cookiesStorage, IProxyProvider? proxyProvider, ILogger logger)
    {
        _cookiesStorage = cookiesStorage;
        _proxyProvider = proxyProvider;
        _logger = logger;
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
                    "Performing page action {current} of {count} with type {actionType}",
                    i, request.PageActions.Count - 1, pageAction.Type);

                await PageActions[pageAction.Type](page, pageAction.Parameters);
            }
        }

        return await page.GetContentAsync();
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
