using WebReaper.Builders;

namespace WebReaper.Playwright;

/// <summary>
/// ADR-0053: registers the Microsoft.Playwright-backed
/// <see cref="PlaywrightPageLoadTransport"/> for the Dynamic
/// (headless-browser) slot. Same shape as the deleted
/// <c>WithPuppeteerPageLoader()</c> from <c>WebReaper.Puppeteer</c>.
/// </summary>
public static class PlaywrightPageLoaderBuilderExtensions
{
    /// <summary>
    /// Use the Microsoft.Playwright transport for Dynamic pages
    /// (<c>CrawlWithBrowser</c> / <c>FollowWithBrowser</c> /
    /// <c>PaginateWithBrowser</c>). First run downloads browser binaries
    /// via the standard <c>playwright install</c> step.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <param name="browser">Which browser engine to launch. Default
    /// <see cref="PlaywrightBrowser.Chromium"/>.</param>
    /// <param name="options">Launch-time options. When <c>null</c>, a
    /// default <see cref="PlaywrightLaunchOptions"/> (headless Chromium,
    /// bundled binary) is used.</param>
    public static ScraperEngineBuilder WithPlaywrightPageLoader(
        this ScraperEngineBuilder builder,
        PlaywrightBrowser browser = PlaywrightBrowser.Chromium,
        PlaywrightLaunchOptions? options = null)
    {
        var resolvedOptions = options ?? new PlaywrightLaunchOptions();
        return builder.WithLoadTransport((cookies, proxy, logger, actionResolver) =>
            new PlaywrightPageLoadTransport(browser, resolvedOptions, cookies, proxy, logger, actionResolver));
    }
}
