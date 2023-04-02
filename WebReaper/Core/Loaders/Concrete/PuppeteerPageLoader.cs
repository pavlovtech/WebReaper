using System.Reflection;
using Microsoft.Extensions.Logging;
using PuppeteerSharp;
using WebReaper.CookieStorage.Abstract;
using WebReaper.Core.Loaders.Abstract;
using WebReaper.Domain.PageActions;
using WebReaper.Extensions;

namespace WebReaper.Core.Loaders.Concrete;

public class PuppeteerPageLoader : BrowserPageLoader, IBrowserPageLoader
{
    private readonly ICookiesStorage _cookiesStorage;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public PuppeteerPageLoader(ILogger logger, ICookiesStorage cookiesStorage) : base(logger)
    {
        _cookiesStorage = cookiesStorage;
    }

    public async Task<string> Load(string url, List<PageAction>? pageActions = null, bool headless = true)
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

        await using var browser = await Puppeteer.LaunchAsync(new LaunchOptions
        {
            Headless = headless,
            ExecutablePath = browserFetcher.RevisionInfo(BrowserFetcher.DefaultChromiumRevision).ExecutablePath
        });

        await using var page = await browser.NewPageAsync();

        var cookies = await _cookiesStorage.GetAsync();

        if (cookies != null)
        {
            var cookieParams = cookies.GetAllCookies().Select(c => new CookieParam
            {
                Name = c.Name,
                Value = c.Value
            }).ToArray();

            await page.SetCookieAsync(cookieParams);
        }

        await page.GoToAsync(url, WaitUntilNavigation.DOMContentLoaded);

        //await page.WaitForNetworkIdleAsync();


        if (pageActions != null)
            foreach (var action in pageActions)
                await PageActions[action.Type](page, action.Parameters);

        var html = await page.GetContentAsync();

        return html;
    }
}