using System.Net;
using System.Net.Security;
using WebReaper.Loaders.Abstract;
using WebReaper.Proxy.Abstract;

namespace WebReaper.Loaders.Concrete
{
    public class ProxyRequests : IHttpRequests
    {
        protected static HttpClient client;

        protected IProxyProvider ProxyProvider { get; }
        public CookieContainer CookieContainer { get; set; }

        public ProxyRequests(IProxyProvider proxyProvider)
        {
            ProxyProvider = proxyProvider;

            if (client == null)
            {
                client = CreateClient();
            }
        }

        protected HttpClient CreateClient()
        {
            var handler = GetHttpHandler();
            return new HttpClient(handler);
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
            return await client.GetAsync(url);
        }
    }
}
