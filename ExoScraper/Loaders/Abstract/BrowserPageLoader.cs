using ExoScraper.PageActions;
using Microsoft.Extensions.Logging;
using PuppeteerSharp;

namespace ExoScraper.Loaders.Abstract;

public abstract class BrowserPageLoader
{
    protected ILogger Logger { get; }

    protected readonly Dictionary<PageActionType, Func<Page, object[], Task>> PageActions = new()
    {
        { PageActionType.ScrollToEnd, async (page, data) => await page.EvaluateExpressionAsync("window.scrollTo(0, document.body.scrollHeight);") },
        { PageActionType.Wait, async (page, data) => await Task.Delay((int)data.First()) },
        { PageActionType.WaitForNetworkIdle, async (page, data) => await page.WaitForNetworkIdleAsync() },
        { PageActionType.Click, async (page, data) => await page.ClickAsync((string)data.First()) }
    };

    public BrowserPageLoader(ILogger logger)
    {
        Logger = logger;
    }
}