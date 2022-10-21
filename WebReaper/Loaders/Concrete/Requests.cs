using System.Net;
using System.Net.Security;
using WebReaper.Loaders.Abstract;

namespace WebReaper.Loaders.Concrete
{
    public class Requests : IHttpRequests
    {
        protected static HttpClient client;

        public CookieContainer CookieContainer { get; set; }

        public Requests()
        {
            client = CreateClient();
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
                CookieContainer = CookieContainer,
                UseCookies = true
            };

            return handler;
        }

        public async Task<HttpResponseMessage> GetAsync(string url)
        {
            return await client.GetAsync(url);
        }
    }
}
