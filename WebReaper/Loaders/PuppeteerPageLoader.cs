using Microsoft.Extensions.Logging;
using PuppeteerSharp;
using WebReaper.Abstractions.Loaders;
using WebReaper.Core.Extensions;

namespace WebReaper.Core.Loaders;

public class PuppeteerPageLoader : IDynamicPageLoader
{
    private ILogger _logger { get; }

    public PuppeteerPageLoader(ILogger logger)
    {
        _logger = logger;
    }

    public async Task<string> Load(string url, string? script)
    {
        using var _ = _logger.LogMethodDuration();

        var browserFetcher = new BrowserFetcher(new BrowserFetcherOptions
        {
            Path = Path.GetTempPath()
        });

        await browserFetcher.DownloadAsync(BrowserFetcher.DefaultChromiumRevision);

        var browser = await Puppeteer.LaunchAsync(new LaunchOptions
        {
            Headless = false,
            ExecutablePath = browserFetcher.RevisionInfo(BrowserFetcher.DefaultChromiumRevision).ExecutablePath
        });

        await using var page = await browser.NewPageAsync();
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