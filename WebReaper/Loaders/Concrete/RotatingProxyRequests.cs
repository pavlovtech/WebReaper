using System.Net;
using System.Net.Security;
using WebReaper.Loaders.Abstract;
using WebReaper.Proxy.Abstract;

namespace WebReaper.Loaders.Concrete
{
    public class RotatingProxyRequests : IHttpRequests
    {
        public IProxyProvider ProxyProvider { get; }

        public CookieContainer CookieContainer { get; set; }

        public RotatingProxyRequests(IProxyProvider proxyProvider)
        {
            ProxyProvider = proxyProvider;
        }

        protected async Task<HttpClient> CreateClient()
        {
            var handler = await GetHttpHandler();
            return new HttpClient(handler);
        }

        public async Task<SocketsHttpHandler> GetHttpHandler()
        {
            var handler = new SocketsHttpHandler()
            {
                SslOptions = new SslClientAuthenticationOptions
                {
                    // Leave certs unvalidated for debugging
                    RemoteCertificateValidationCallback = delegate { return true; },
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

        public async Task<HttpResponseMessage> GetAsync(string url)
        {
            var client = await CreateClient();
            var resp = await client.GetAsync(url);

            client.Dispose();

            return resp;
        }
    }
}
