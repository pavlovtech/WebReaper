using System.Net;
using System.Net.Security;
using System.Text;
using Microsoft.Extensions.Logging;
using WebReaper.Core.CookieStorage.Abstract;
using WebReaper.Core.Loaders.Abstract;
using WebReaper.Extensions;
using WebReaper.Proxy.Abstract;

namespace WebReaper.Core.Loaders.Concrete;

/// <summary>
/// HTTP <see cref="IPageLoadTransport"/> — the one home for the HTTP client /
/// handler (ADR 0004). Replaces <c>HttpStaticPageLoader</c> and the
/// <c>PageRequester</c> / <c>ProxyPageRequester</c> /
/// <c>RotatingProxyPageRequester</c> triad. The handler is built <em>after</em>
/// the cookies are fetched, so stored cookies are actually applied — the bug
/// the old non-proxy path had (handler built in the requester's constructor,
/// cookie container set afterwards). One canonical User-Agent (the triad had
/// two by copy-drift). Client is per-request, mirroring the only correct
/// member of the old triad (the rotating one); this trades connection pooling
/// for correct per-request cookie/proxy application — connection-pool tuning
/// is a separate concern, out of scope for this deepening.
/// </summary>
internal class HttpPageLoadTransport : IPageLoadTransport
{
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/106.0.0.0 Safari/537.36";

    private readonly ICookiesStorage _cookiesStorage;
    private readonly IProxyProvider? _proxyProvider;
    private readonly ILogger _logger;
    private readonly Func<HttpMessageHandler>? _handlerFactory;

    public HttpPageLoadTransport(ICookiesStorage cookiesStorage, IProxyProvider? proxyProvider, ILogger logger)
        : this(cookiesStorage, proxyProvider, logger, handlerFactory: null)
    {
    }

    // Test seam (ADR-0083): inject a stub HttpMessageHandler so the response-
    // handling behaviour (non-2xx returned as data, header capture, the
    // no-response PageLoadException) is unit-testable without a live server.
    // Mirrors SiteMapper's handler-factory pattern. A null factory (production)
    // builds the per-request SocketsHttpHandler with cookies and the optional
    // proxy applied; a supplied factory bypasses that and uses the stub.
    internal HttpPageLoadTransport(
        ICookiesStorage cookiesStorage,
        IProxyProvider? proxyProvider,
        ILogger logger,
        Func<HttpMessageHandler>? handlerFactory)
    {
        // ServicePointManager.DefaultConnectionLimit / .ServerCertificateValidationCallback
        // were obsolete (SYSLIB0014) and inert (they do not affect
        // SocketsHttpHandler), so the connection limit and cert-bypass live on
        // the per-request SocketsHttpHandler in LoadAsync instead.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        _cookiesStorage = cookiesStorage;
        _proxyProvider = proxyProvider;
        _logger = logger;
        _handlerFactory = handlerFactory;
    }

    public async Task<PageLoadResult> LoadAsync(PageRequest request, CancellationToken cancellationToken = default)
    {
        using var _ = _logger.LogMethodDuration();

        HttpMessageHandler handler;
        if (_handlerFactory is not null)
        {
            handler = _handlerFactory();
        }
        else
        {
            var cookies = await _cookiesStorage.GetAsync();

            var socketsHandler = new SocketsHttpHandler
            {
                MaxConnectionsPerServer = 10000,
                SslOptions = new SslClientAuthenticationOptions
                {
                    // Leave certs unvalidated for parity with the removed requesters.
                    RemoteCertificateValidationCallback = delegate { return true; }
                },
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                PooledConnectionLifetime = Timeout.InfiniteTimeSpan,
                UseCookies = true,
                CookieContainer = cookies
            };

            if (_proxyProvider is not null)
            {
                socketsHandler.UseProxy = true;
                socketsHandler.Proxy = await _proxyProvider.GetProxyAsync();
            }

            handler = socketsHandler;
        }

        using var client = new HttpClient(handler);
        client.DefaultRequestHeaders.Add("User-Agent", UserAgent);

        HttpResponseMessage response;
        try
        {
            response = await client.GetAsync(request.Url, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            // No response at all (DNS failure, connection refused, TLS error).
            // ADR-0083: a transport fault, not a status to report.
            _logger.LogError(ex, "No response loading page {url}", request.Url);
            throw new PageLoadException($"No response from {request.Url}: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // A timeout (the caller did not cancel) is also a transport fault.
            // Genuine caller cancellation propagates as the original
            // OperationCanceledException so the retry policy does not retry it.
            throw new PageLoadException($"Timed out loading {request.Url}.", ex);
        }

        using (response)
        {
            // ADR-0083: a completed response is data, whatever its status. The
            // body, status, and headers all flow back; a non-2xx no longer
            // throws (the pre-0083 InvalidOperationException, which discarded the
            // status into Exception.Data, is gone). The block detector and the
            // CLI escalation read the status/headers in later slices.
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                _logger.LogWarning(
                    "Page {url} returned status {statusCode}", request.Url, (int)response.StatusCode);

            return new PageLoadResult
            {
                Html = body,
                HttpStatus = (int)response.StatusCode,
                Headers = CollectHeaders(response),
            };
        }
    }

    // Flatten response + content headers into one case-insensitive map. A
    // multi-value header is joined with ", " (the block detector substring-
    // matches, so the exact join is immaterial).
    private static IReadOnlyDictionary<string, string> CollectHeaders(HttpResponseMessage response)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in response.Headers)
            headers[h.Key] = string.Join(", ", h.Value);
        foreach (var h in response.Content.Headers)
            headers[h.Key] = string.Join(", ", h.Value);
        return headers;
    }
}
