using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net;
using WebReaper.Proxy.Abstract;

namespace WebReaper.ProxyProviders.WebShareProxy
{

    public class WebShareProxyProvider : IProxyProvider
    {
        private readonly string proxyApiKey = "a45n2ninq91ccjrjrrmgr1okqxiyte7iy4uw0kds";
        private readonly string getProxiesUrl = "https://proxy.webshare.io/api/proxy/list/";
        static readonly HttpClient client = new HttpClient();

        private List<WebProxy> webProxies = new List<WebProxy>();
        private Random rnd = new Random();

        public Task Initialization { get; private set; }

        public WebShareProxyProvider()
        {
            Initialization = InitAsync();
        }

        async Task InitAsync()
        {
            var proxies = await GetWebProxies();
            webProxies.AddRange(proxies);
        }

        public async Task<WebProxy> GetProxyAsync()
        {
            await Initialization;

            int index = rnd.Next(0, webProxies.Count);

            return webProxies[index];
        }

        private async Task<List<Proxy>> GetProxies()
        {
            var proxies = new List<Proxy>();

            client.DefaultRequestHeaders.Add("Authorization", proxyApiKey);

            for (int page = 0; page < 10; page++)
            {
                var proxiesResp = await client.GetAsync(getProxiesUrl + $"?page={page}");

                JObject json = JObject.Parse(await proxiesResp.Content.ReadAsStringAsync());

                List<Proxy>? m = JsonConvert.DeserializeObject<List<Proxy>>(json!["results"]!.ToString());

                if (m != null)
                {
                    proxies.AddRange(m);
                }
            }

            return proxies;
        }

        private async Task<List<WebProxy>> GetWebProxies()
        {
            var proxiesRaw = await GetProxies();

            var proxies = proxiesRaw.Select(p => new WebProxy
            {
                Address = new Uri($"http://{p.Proxy_Address}:{p.Ports["http"]}"),
                BypassProxyOnLocal = false,
                UseDefaultCredentials = false,

                // *** These creds are given to the proxy server, not the web server ***
                Credentials = new NetworkCredential(
                userName: p.Username,
                password: p.Password)
            });

            return proxies.ToList();
        }
    }
}
