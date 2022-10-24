using System.Net;
using System.Net.Security;
using WebReaper.Loaders.Abstract;

namespace WebReaper.Loaders.Concrete
{
    public class Requests : IHttpRequests
    {
        protected static HttpClient? client;

        public CookieContainer CookieContainer { get; set; }

        public Requests()
        {
            client = CreateClient();
        }

        protected HttpClient CreateClient()
        {
            var handler = GetHttpHandler();
            var client = new HttpClient(handler);
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/106.0.0.0 Safari/537.36");
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
                CookieContainer = CookieContainer,
                UseCookies = true
            };

            return handler;
        }

        public async Task<HttpResponseMessage> GetAsync(string url)
        {
            return await client!.GetAsync(url);
        }
    }
}
