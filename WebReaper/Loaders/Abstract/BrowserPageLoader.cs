using Microsoft.Extensions.Logging;
using PuppeteerSharp;
using WebReaper.PageActions;

namespace WebReaper.Loaders.Abstract
{
    public abstract class BrowserPageLoader
    {
        protected ILogger Logger { get; }

        protected Dictionary<PageActionType, Func<Page, object[], Task>> PageActions = new()
        {
            { PageActionType.ScrollToEnd, async (page, data) => await page.EvaluateExpressionAsync("window.scrollTo(0, document.body.scrollHeight);") },
            { PageActionType.Wait, async (page, data) => await Task.Delay((int)data.First()) },
            { PageActionType.Click, async (page, data) => await page.ClickAsync((string)data.First()) }
        };

        public BrowserPageLoader(ILogger logger)
        {
            Logger = logger;
        }
    }
}
