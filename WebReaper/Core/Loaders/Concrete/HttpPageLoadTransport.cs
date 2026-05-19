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
public class HttpPageLoadTransport : IPageLoadTransport
{
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/106.0.0.0 Safari/537.36";

    private readonly ICookiesStorage _cookiesStorage;
    private readonly IProxyProvider? _proxyProvider;
    private readonly ILogger _logger;

    public HttpPageLoadTransport(ICookiesStorage cookiesStorage, IProxyProvider? proxyProvider, ILogger logger)
    {
        // The connection limit and cert-bypass live on the per-request
        // SocketsHttpHandler in LoadAsync (MaxConnectionsPerServer +
        // SslOptions.RemoteCertificateValidationCallback). The old
        // ServicePointManager.DefaultConnectionLimit / .ServerCertificateValidationCallback
        // calls were obsolete (SYSLIB0014) AND inert — ServicePointManager
        // does not affect HttpClient/SocketsHttpHandler — so they are removed,
        // not ported. No behavioural change to the request path.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        _cookiesStorage = cookiesStorage;
        _proxyProvider = proxyProvider;
        _logger = logger;
    }

    public async Task<string> LoadAsync(PageRequest request, CancellationToken cancellationToken = default)
    {
        using var _ = _logger.LogMethodDuration();

        var cookies = await _cookiesStorage.GetAsync();

        var handler = new SocketsHttpHandler
        {
            MaxConnectionsPerServer = 10000,
            SslOptions = new SslClientAuthenticationOptions
            {
                // Leave certs unvalidated — parity with the removed requesters.
                RemoteCertificateValidationCallback = delegate { return true; }
            },
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            PooledConnectionLifetime = Timeout.InfiniteTimeSpan,
            UseCookies = true,
            CookieContainer = cookies
        };

        if (_proxyProvider is not null)
        {
            handler.UseProxy = true;
            handler.Proxy = await _proxyProvider.GetProxyAsync();
        }

        using var client = new HttpClient(handler);
        client.DefaultRequestHeaders.Add("User-Agent", UserAgent);

        var response = await client.GetAsync(request.Url, cancellationToken);

        if (response.IsSuccessStatusCode)
            return await response.Content.ReadAsStringAsync(cancellationToken);

        _logger.LogError("Failed to load page {url}. Error code: {statusCode}", request.Url, response.StatusCode);

        throw new InvalidOperationException($"Failed to load page {request.Url}. Error code: {response.StatusCode}")
        {
            Data = { ["url"] = request.Url, ["statusCode"] = response.StatusCode }
        };
    }
}
