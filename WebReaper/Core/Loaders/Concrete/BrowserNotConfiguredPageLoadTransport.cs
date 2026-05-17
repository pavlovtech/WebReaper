using WebReaper.Core.Loaders.Abstract;

namespace WebReaper.Core.Loaders.Concrete;

/// <summary>
/// The default dynamic-page <see cref="IPageLoadTransport"/> when no browser
/// transport is registered (ADR-0009). Core is HTTP-only by default; the
/// headless-browser transport moved to the WebReaper.Puppeteer satellite.
/// A Dynamic-page load with no <c>WithLoadTransport</c> registration (i.e.
/// no <c>.WithPuppeteerPageLoader()</c>) fails here with an actionable
/// message rather than a null-ref. ADR-0004's one-<see cref="IPageLoader"/>
/// / two-<see cref="IPageLoadTransport"/> dispatcher is unchanged — only the
/// default composition of the dynamic slot moved out of core.
/// </summary>
public sealed class BrowserNotConfiguredPageLoadTransport : IPageLoadTransport
{
    public Task<string> LoadAsync(PageRequest request, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(
            "Dynamic (headless-browser) page loading requires the WebReaper.Puppeteer package. " +
            "Add a reference to WebReaper.Puppeteer and call .WithPuppeteerPageLoader() " +
            "(using WebReaper.Puppeteer) on the builder before crawling Dynamic pages " +
            "(GetWithBrowser / FollowWithBrowser / PaginateWithBrowser). See ADR-0009.");
}
