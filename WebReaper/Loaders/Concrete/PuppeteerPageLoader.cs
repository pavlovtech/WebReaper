using System.Collections.Immutable;
using System.Net;
using System.Reflection;
using Microsoft.Extensions.Logging;
using PuppeteerSharp;
using WebReaper.Extensions;
using WebReaper.Loaders.Abstract;
using WebReaper.PageActions;

namespace WebReaper.Loaders.Concrete;

public class PuppeteerPageLoader : IBrowserPageLoader
{
    private readonly CookieContainer? _cookies;
    private ILogger Logger { get; }
    
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    private Dictionary<PageActionType, Func<Page, object[], Task>> _actionTypeToAction = new()
    {
        { PageActionType.ScrollToEnd, async (page, data) => await page.EvaluateExpressionAsync("") },
        { PageActionType.Wait, async (page, data) => await Task.Delay((int)data.First()) },
        { PageActionType.Repeat, async (page, data) => await Task.Delay((int)data.First()) }
    };

    public PuppeteerPageLoader(ILogger logger, CookieContainer? cookies)
    {
        _cookies = cookies;
        Logger = logger;
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

        /*
        page
	.Click('.test')
	.Wait(100)
	.ScrollToEnd()
	.RepeatWithDelay(10, 1000)
	.EvaluateExpression()
	.ScrollTo('fdas')
	.WaitForSelector('dfs')
        */

        await page.GoToAsync(url, WaitUntilNavigation.DOMContentLoaded);

        //await page.WaitForNetworkIdleAsync();


        if(pageActions != null)
        {
            foreach (var action in pageActions)
            {
                await _actionTypeToAction[action.Type](page, action.Parameters);
            }
        }

        var html = await page.GetContentAsync();

        return html;
    }
}