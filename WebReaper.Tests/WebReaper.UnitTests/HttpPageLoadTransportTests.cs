using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using WebReaper.Core.CookieStorage.Concrete;
using WebReaper.Core.Loaders.Abstract;
using WebReaper.Core.Loaders.Concrete;
using WebReaper.Domain.Selectors;

namespace WebReaper.UnitTests;

// ADR-0083 slice 1: the HTTP transport's response-handling contract, exercised
// through a stubbed HttpMessageHandler (the SiteMapper handler-factory pattern,
// reused here via HttpPageLoadTransport's internal test-seam constructor). No
// live server, no network. Pins: a completed non-2xx is returned as data (not
// thrown), the status and headers are surfaced, and a genuine no-response
// failure throws PageLoadException.
public class HttpPageLoadTransportTests
{
    // Returns a canned response, or faults the send (the no-response case).
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            try { return Task.FromResult(responder(request)); }
            catch (Exception ex) { return Task.FromException<HttpResponseMessage>(ex); }
        }
    }

    private static HttpPageLoadTransport TransportWith(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        new(new InMemoryCookieStorage(), proxyProvider: null, NullLogger.Instance,
            () => new StubHandler(responder));

    [Theory]
    [InlineData(403)]
    [InlineData(429)]
    [InlineData(503)]
    public async Task A_completed_non_2xx_is_returned_as_data_not_thrown(int status)
    {
        var transport = TransportWith(_ => new HttpResponseMessage((HttpStatusCode)status)
        {
            Content = new StringContent("<html>Just a moment...</html>")
        });

        var result = await transport.LoadAsync(new PageRequest("https://x.test/", PageType.Static));

        Assert.Equal(status, result.HttpStatus);
        Assert.Equal("<html>Just a moment...</html>", result.Html);
    }

    [Fact]
    public async Task A_2xx_response_surfaces_status_and_headers()
    {
        var transport = TransportWith(_ =>
        {
            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html>ok</html>")
            };
            // A response header and a content header, to prove both are folded in.
            resp.Headers.TryAddWithoutValidation("CF-RAY", "abc123");
            return resp;
        });

        var result = await transport.LoadAsync(new PageRequest("https://x.test/", PageType.Static));

        Assert.Equal(200, result.HttpStatus);
        Assert.Equal("<html>ok</html>", result.Html);
        // Header lookup is case-insensitive (the detector matches that way).
        Assert.True(result.Headers.ContainsKey("cf-ray"));
        Assert.Equal("abc123", result.Headers["cf-ray"]);
        Assert.True(result.Headers.ContainsKey("Content-Type"));
    }

    [Fact]
    public async Task A_no_response_failure_throws_PageLoadException()
    {
        var transport = TransportWith(_ =>
            throw new HttpRequestException("Connection refused"));

        await Assert.ThrowsAsync<PageLoadException>(() =>
            transport.LoadAsync(new PageRequest("https://x.test/", PageType.Static)));
    }
}
