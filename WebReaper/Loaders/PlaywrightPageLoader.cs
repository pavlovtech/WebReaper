using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using WebReaper.Abstractions.Loaders.PageLoader;
using WebReaper.Extensions;

public class PlaywrightPageLoader : IPageLoader
{
    private ILogger _logger { get; }

    public PlaywrightPageLoader(ILogger logger)
    {
        _logger = logger;
    }

    public async Task<string> Load(string url)
    {
        using (_logger.LogMethodDuration())
        {
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync();
            var page = await browser.NewPageAsync();
            await page.GotoAsync(url);
            var html = await page.ContentAsync();
            return html;
        }
    }
}