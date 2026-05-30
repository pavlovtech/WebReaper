using Microsoft.Extensions.Logging.Abstractions;
using WebReaper.Core.Loaders.Abstract;
using WebReaper.Core.Loaders.Concrete;
using WebReaper.Domain.Selectors;

namespace WebReaper.UnitTests;

// The page-loader dispatcher, tested through its interface — the one piece of
// pure logic this deepening adds (it replaces the Spider's old PageType
// switch, ADR 0004). Fake transports record what they were handed; no HTTP,
// no browser. Pins: Static → HTTP transport, Dynamic → browser transport, and
// the PageRequest is forwarded unchanged.
public class PageLoaderTests
{
    private sealed class FakeTransport(string html) : IPageLoadTransport
    {
        public PageRequest? Received { get; private set; }

        public Task<PageLoadResult> LoadAsync(PageRequest request, CancellationToken cancellationToken = default)
        {
            Received = request;
            return Task.FromResult(new PageLoadResult { Html = html });
        }
    }

    [Fact]
    public async Task Static_request_dispatches_to_the_http_transport()
    {
        var http = new FakeTransport("HTTP");
        var browser = new FakeTransport("BROWSER");
        var loader = new PageLoader(http, browser, NullLogger.Instance);

        var result = await loader.LoadAsync(new PageRequest("https://x.test", PageType.Static));

        Assert.Equal("HTTP", result.Html);
        Assert.Equal("https://x.test", http.Received!.Url);
        Assert.Null(browser.Received);
    }

    [Fact]
    public async Task Dynamic_request_dispatches_to_the_browser_transport()
    {
        var http = new FakeTransport("HTTP");
        var browser = new FakeTransport("BROWSER");
        var loader = new PageLoader(http, browser, NullLogger.Instance);

        var result = await loader.LoadAsync(new PageRequest("https://x.test", PageType.Dynamic));

        Assert.Equal("BROWSER", result.Html);
        Assert.Equal("https://x.test", browser.Received!.Url);
        Assert.Null(http.Received);
    }

    [Fact]
    public async Task The_request_is_forwarded_unchanged()
    {
        var browser = new FakeTransport("BROWSER");
        var loader = new PageLoader(new FakeTransport("HTTP"), browser, NullLogger.Instance);
        var request = new PageRequest("https://x.test/p", PageType.Dynamic, Headless: false);

        await loader.LoadAsync(request);

        Assert.Same(request, browser.Received);
    }
}
