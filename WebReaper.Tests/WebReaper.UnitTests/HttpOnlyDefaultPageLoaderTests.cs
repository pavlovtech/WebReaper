using WebReaper.Core.Loaders.Abstract;
using WebReaper.Core.Loaders.Concrete;
using WebReaper.Domain.Selectors;

namespace WebReaper.UnitTests;

public class HttpOnlyDefaultPageLoaderTests
{
    // ADR-0009: core is HTTP-only by default. The headless-browser transport
    // moved to the WebReaper.Puppeteer satellite, so a PageType.Dynamic load
    // with no WithLoadTransport registration (i.e. no .WithPuppeteerPageLoader())
    // must fail with an actionable message pointing at WebReaper.Puppeteer,
    // not a NullReferenceException. ADR-0004's one-IPageLoader /
    // two-IPageLoadTransport dispatcher is unchanged — only the default
    // composition of the dynamic slot.
    [Fact]
    public async Task Dynamic_page_without_a_registered_browser_transport_throws_pointing_at_WebReaper_Puppeteer()
    {
        var transport = new BrowserNotConfiguredPageLoadTransport();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            transport.LoadAsync(new PageRequest("https://example.com", PageType.Dynamic)));

        Assert.Contains("WebReaper.Puppeteer", ex.Message);
    }
}
