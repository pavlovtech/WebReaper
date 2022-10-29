using System.Net;
using Microsoft.Extensions.Logging;
using PuppeteerSharp;
using WebReaper.Extensions;
using WebReaper.Loaders.Abstract;

namespace WebReaper.Loaders.Concrete;

public class PuppeteerPageLoader : IDynamicPageLoader
{
    private readonly CookieContainer? _cookies;
    private ILogger Logger { get; }
    
    private readonly SemaphoreSlim _semaphore = new(1, 1);

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

        await using var browser = await Puppeteer.LaunchAsync(new LaunchOptions
        {
            Headless = true,
            ExecutablePath = browserFetcher.RevisionInfo(BrowserFetcher.DefaultChromiumRevision).ExecutablePath
        });

        await using var page = await browser.NewPageAsync();

        if (_cookies != null)
        {
            var cookieParams = _cookies.GetAllCookies().Select(c => new CookieParam
            {
                Name = c.Name,
                Value = c.Value
            }).ToArray();

            await page.SetCookieAsync(cookieParams);
        }

        await page.GoToAsync(url, WaitUntilNavigation.DOMContentLoaded);

        //await page.WaitForNetworkIdleAsync();

        if (script != null)
        {
            await page.EvaluateExpressionAsync(script);
        }

        var html = await page.GetContentAsync();

        return html;
    }
}