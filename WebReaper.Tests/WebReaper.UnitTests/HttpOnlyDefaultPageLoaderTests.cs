using WebReaper.Core.Loaders.Abstract;
using WebReaper.Core.Loaders.Concrete;
using WebReaper.Domain.Selectors;

namespace WebReaper.UnitTests;

public class HttpOnlyDefaultPageLoaderTests
{
    // ADR-0009: core is HTTP-only by default. The headless-browser transport
    // ships in one of two satellites (ADR-0052/0053): WebReaper.Playwright
    // (Microsoft.Playwright SDK; the modern default) or WebReaper.Cdp (raw CDP;
    // AOT-clean; bedrock for stealth backends). A PageType.Dynamic load with
    // no WithLoadTransport registration must fail with an actionable message
    // naming both options, not a NullReferenceException. ADR-0004's
    // one-IPageLoader / two-IPageLoadTransport dispatcher is unchanged — only
    // the default composition of the dynamic slot.
    [Fact]
    public async Task Dynamic_page_without_a_registered_browser_transport_throws_naming_both_satellites()
    {
        var transport = new BrowserNotConfiguredPageLoadTransport();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            transport.LoadAsync(new PageRequest("https://example.com", PageType.Dynamic)));

        Assert.Contains("WebReaper.Playwright", ex.Message);
        Assert.Contains("WebReaper.Cdp", ex.Message);
    }
}
