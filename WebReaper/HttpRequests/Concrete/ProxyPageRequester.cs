using System.Net;
using System.Net.Security;
using WebReaper.HttpRequests.Abstract;
using WebReaper.Proxy.Abstract;

namespace WebReaper.HttpRequests.Concrete;

public class ProxyPageRequester : IPageRequester
{
    private static HttpClient? client;

    public ProxyPageRequester(IProxyProvider proxyProvider)
    {
        ProxyProvider = proxyProvider;

        client ??= CreateClient();
    }

    private IProxyProvider ProxyProvider { get; }
    public CookieContainer CookieContainer { get; set; } = new();

    public async Task<HttpResponseMessage> GetAsync(string url)
    {
        return await client!.GetAsync(url);
    }

    private HttpClient CreateClient()
    {
        var handler = GetHttpHandler();
        var client = new HttpClient(handler);
        client.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 6.2) AppleWebKit/535.7 (KHTML, like Gecko) Comodo_Dragon/16.1.1.0 Chrome/16.0.912.63 Safari/535.7");
        return client;
    }

    private SocketsHttpHandler GetHttpHandler()
    {
        var handler = new SocketsHttpHandler
        {
            MaxConnectionsPerServer = 10000,
            SslOptions = new SslClientAuthenticationOptions
            {
                // Leave certs unvalidated for debugging
                RemoteCertificateValidationCallback = delegate { return true; }
            },
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            PooledConnectionLifetime = Timeout.InfiniteTimeSpan,
            UseProxy = true,
            UseCookies = true,
            CookieContainer = CookieContainer,
            Proxy = ProxyProvider.GetProxyAsync().GetAwaiter().GetResult()
        };

        return handler;
    }
}