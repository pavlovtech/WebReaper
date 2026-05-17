using Xunit;
using WebReaper.Builders;
using WebReaper.Puppeteer;

namespace WebReaper.Puppeteer.Tests;

public class WithPuppeteerPageLoaderExtensionTests
{
    // ADR-0009 satellite contract: .WithPuppeteerPageLoader() is an extension
    // over ScraperEngineBuilder's public WithLoadTransport registration seam,
    // shipped here in WebReaper.Puppeteer, not in core (core is HTTP-only by
    // default and no longer references PuppeteerSharp). Offline-safe:
    // registering the transport factory neither constructs the transport nor
    // touches Chromium — that happens lazily at crawl time on a Dynamic page.
    [Fact]
    public void WithPuppeteerPageLoader_is_a_ScraperEngineBuilder_extension_that_chains()
    {
        var builder = new ScraperEngineBuilder();

        var result = builder.WithPuppeteerPageLoader();

        Assert.Same(builder, result);
    }
}
