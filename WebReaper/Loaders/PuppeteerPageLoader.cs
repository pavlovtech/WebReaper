using Microsoft.Extensions.Logging;
using PuppeteerSharp;
using WebReaper.Abstractions.Loaders.PageLoader;
using WebReaper.Extensions;

public class PuppeteerPageLoader : IPageLoader
{
    private ILogger _logger { get; }

    public PuppeteerPageLoader(ILogger logger)
    {
        _logger = logger;
    }

    public async Task<string> Load(string url)
    {
        using var _ = _logger.LogMethodDuration();

        using var browserFetcher = new BrowserFetcher();
        await browserFetcher.DownloadAsync();
        await using var browser = await Puppeteer.LaunchAsync(
            new LaunchOptions { Headless = true });
        await using var page = await browser.NewPageAsync();
        await page.GoToAsync(url, WaitUntilNavigation.DOMContentLoaded);

        //await page.WaitForNetworkIdleAsync();

        var html = await page.GetContentAsync();
        return html;
    }
}