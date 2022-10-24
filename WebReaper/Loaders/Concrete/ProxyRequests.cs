using System.Net;
using System.Net.Security;
using WebReaper.Loaders.Abstract;
using WebReaper.Proxy.Abstract;

namespace WebReaper.Loaders.Concrete
{
    public class ProxyRequests : IHttpRequests
    {
        private static HttpClient? client;

        protected IProxyProvider ProxyProvider { get; }
        public CookieContainer CookieContainer { get; set; } = new CookieContainer();

        public ProxyRequests(IProxyProvider proxyProvider)
        {
            ProxyProvider = proxyProvider;

            client ??= CreateClient();
        }

        protected HttpClient CreateClient()
        {
            var handler = GetHttpHandler();
            var client = new HttpClient(handler);
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 6.2) AppleWebKit/535.7 (KHTML, like Gecko) Comodo_Dragon/16.1.1.0 Chrome/16.0.912.63 Safari/535.7");
            return client;
        }

        protected SocketsHttpHandler GetHttpHandler()
        {
            var handler = new SocketsHttpHandler()
            {
                MaxConnectionsPerServer = 10000,
                SslOptions = new SslClientAuthenticationOptions
                {
                    // Leave certs unvalidated for debugging
                    RemoteCertificateValidationCallback = delegate { return true; },
                },
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                PooledConnectionLifetime = Timeout.InfiniteTimeSpan,
                UseProxy = true,
                UseCookies = true,
                CookieContainer = CookieContainer
            };

            var proxy = ProxyProvider.GetProxyAsync().GetAwaiter().GetResult();

            return handler;
        }

        public async Task<HttpResponseMessage> GetAsync(string url)
        {
            return await client!.GetAsync(url);
        }
    }
}
