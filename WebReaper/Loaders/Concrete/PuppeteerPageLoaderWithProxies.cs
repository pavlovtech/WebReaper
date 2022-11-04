using System.Net;
using Microsoft.Extensions.Logging;
using PuppeteerExtraSharp.Plugins.ExtraStealth;
using PuppeteerExtraSharp;
using PuppeteerSharp;
using WebReaper.Extensions;
using WebReaper.Loaders.Abstract;
using WebReaper.Proxy.Abstract;
using System.Collections.Immutable;
using WebReaper.PageActions;
using System.Reflection;

namespace WebReaper.Loaders.Concrete;

public class PuppeteerPageLoaderWithProxies : BrowserPageLoader, IBrowserPageLoader
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    private readonly IProxyProvider _proxyProvider;
    private readonly CookieContainer? _cookies;

    public PuppeteerPageLoaderWithProxies(ILogger logger, IProxyProvider proxyProvider, CookieContainer? cookies):base(logger)
    {
        _proxyProvider = proxyProvider;
        _cookies = cookies;
    }

    public async Task<string> Load(string url, ImmutableQueue<PageAction>? pageActions = null)
    {
        using var _ = Logger.LogMethodDuration();

        var browserFetcher = new BrowserFetcher(new BrowserFetcherOptions
        {
            Path = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
        });

        await _semaphore.WaitAsync();
        try
        {
            await browserFetcher.DownloadAsync(BrowserFetcher.DefaultChromiumRevision);
        }
        finally
        {
            _semaphore.Release();
        }

        var puppeteerExtra = new PuppeteerExtra().Use(new StealthPlugin());
        
        var proxy = await _proxyProvider.GetProxyAsync();
        var proxyAddress = $"--proxy-server={proxy!.Address!.Host}:{proxy.Address.Port}";

        await using var browser = await puppeteerExtra.LaunchAsync(new LaunchOptions
        {
            Headless = false,
            ExecutablePath = browserFetcher.RevisionInfo(BrowserFetcher.DefaultChromiumRevision).ExecutablePath,
            Args = new[]
            {
                "--disable-dev-shm-usage",
                "--no-sandbox",
                "--disable-setuid-sandbox",
                proxyAddress
            }
        });

        await using var page = await browser.NewPageAsync();
        
        var creds = proxy.Credentials?.GetCredential(new Uri(proxy.Address.ToString()), string.Empty);

        await page.AuthenticateAsync(new Credentials
        {
            Username = creds?.UserName,
            Password = creds?.Password
        });

        if (_cookies != null)
        {
            var cookieParams = _cookies.GetAllCookies().Select(c => new CookieParam
            {
                Name = c.Name,
                Value = c.Value,
                Domain = c.Domain,
                Secure = c.Secure
            }).ToArray();

            await page.SetCookieAsync(cookieParams);
        }

        await page.GoToAsync(url, WaitUntilNavigation.Networkidle2);

        if (pageActions != null)
        {
            foreach (var action in pageActions)
            {
                await PageActions[action.Type](page, action.Parameters);
            }
        }

        var html = await page.GetContentAsync();

        return html;
    }
}