using System.Net;
using System.Net.Security;
using WebReaper.HttpRequests.Abstract;
using WebReaper.Proxy.Abstract;

namespace WebReaper.HttpRequests.Concrete;

public class RotatingProxyPageRequester : IPageRequester
{
    public RotatingProxyPageRequester(IProxyProvider proxyProvider)
    {
        ProxyProvider = proxyProvider;
    }

    public IProxyProvider ProxyProvider { get; }

    public CookieContainer CookieContainer { get; set; }

    public async Task<HttpResponseMessage> GetAsync(string url)
    {
        var client = await CreateClient();
        var resp = await client.GetAsync(url);

        client.Dispose();

        return resp;
    }

    private async Task<HttpClient> CreateClient()
    {
        var handler = await GetHttpHandler();
        var client = new HttpClient(handler);
        client.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 6.2) AppleWebKit/535.7 (KHTML, like Gecko) Comodo_Dragon/16.1.1.0 Chrome/16.0.912.63 Safari/535.7");
        return client;
    }

    public async Task<SocketsHttpHandler> GetHttpHandler()
    {
        var handler = new SocketsHttpHandler
        {
            SslOptions = new SslClientAuthenticationOptions
            {
                // Leave certs unvalidated for debugging
                RemoteCertificateValidationCallback = delegate { return true; }
            },
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(5),
            PooledConnectionLifetime = TimeSpan.FromTicks(0),
            UseCookies = true,
            UseProxy = true,
            Proxy = await ProxyProvider.GetProxyAsync(),
            CookieContainer = CookieContainer
        };

        return handler;
    }
}