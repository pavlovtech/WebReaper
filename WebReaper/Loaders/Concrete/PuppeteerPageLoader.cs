using System.Net;
using Microsoft.Extensions.Logging;
using PuppeteerExtraSharp.Plugins.ExtraStealth;
using PuppeteerExtraSharp;
using PuppeteerSharp;
using WebReaper.Extensions;
using WebReaper.Loaders.Abstract;
using WebReaper.Proxy.Abstract;
using Azure;

namespace WebReaper.Loaders.Concrete;

public class PuppeteerPageLoader : IDynamicPageLoader
{
    public IProxyProvider? ProxyProvider { get; set; }

    public bool IsProxyEnabled => ProxyProvider != null;

    private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

    private readonly CookieContainer? _cookies;
    private ILogger Logger { get; }

    public PuppeteerPageLoader(ILogger logger, CookieContainer? cookies)
    {
        _cookies = cookies;
        Logger = logger;
    }

    public async Task<string> Load(string url, string? script)
    {
        using var _ = Logger.LogMethodDuration();

        var browserFetcher = new BrowserFetcher(new BrowserFetcherOptions
        {
            Path = Path.GetTempPath()
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

        Page page;

        if (IsProxyEnabled)
        {
            page = await GetBrowserPageWithProxy(puppeteerExtra, browserFetcher);
        } 
        else
        {
            await using var browser = await puppeteerExtra.LaunchAsync(new LaunchOptions
            {
                Headless = false,
                ExecutablePath = browserFetcher.RevisionInfo(BrowserFetcher.DefaultChromiumRevision).ExecutablePath,
            });

            page = await browser.NewPageAsync();
        }

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

        if (script != null)
        {
            await page.EvaluateExpressionAsync(script);
        }

        var html = await page.GetContentAsync();

        page.Dispose();

        return html;
    }

    private async Task<Page> GetBrowserPageWithProxy(PuppeteerExtra puppeteerExtra, BrowserFetcher browserFetcher)
    {
        var proxy = await ProxyProvider!.GetProxyAsync();
        var proxyAddress = $"--proxy-server={proxy!.Address!.Host}:{proxy.Address.Port}";

        await using var browser = await puppeteerExtra.LaunchAsync(new LaunchOptions
        {
            Headless = false,
            ExecutablePath = browserFetcher.RevisionInfo(BrowserFetcher.DefaultChromiumRevision).ExecutablePath,
            Args = new string[]
            {
                    "--disable-dev-shm-usage",
                    "--no-sandbox",
                    "--disable-setuid-sandbox",
                    proxyAddress
            }
        });

        var page = await browser.NewPageAsync();

        var creds = proxy?.Credentials?.GetCredential(new Uri(proxy.Address.ToString()), string.Empty);

        await page.AuthenticateAsync(new Credentials()
        {
            Username = creds?.UserName,
            Password = creds?.Password
        });

        return page;
    }
}