using Microsoft.Extensions.Logging;
using PuppeteerSharp;
using WebReaper.Abstractions.Loaders.PageLoader;
using WebReaper.Extensions;

namespace WebReaper.Loaders;

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

        var browserFetcher = new BrowserFetcher(new BrowserFetcherOptions
        {
            Path = Path.GetTempPath()
        });

        await browserFetcher.DownloadAsync(BrowserFetcher.DefaultChromiumRevision);

        Browser browser = await Puppeteer.LaunchAsync(new LaunchOptions
        {
            Headless = true,
            ExecutablePath = browserFetcher.RevisionInfo(BrowserFetcher.DefaultChromiumRevision.ToString()).ExecutablePath
        });

        await using var page = await browser.NewPageAsync();
        await page.GoToAsync(url, WaitUntilNavigation.DOMContentLoaded);

        //await page.WaitForNetworkIdleAsync();

        var html = await page.GetContentAsync();
        return html;
    }
}