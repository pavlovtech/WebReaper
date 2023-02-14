using System.Net;

namespace ExoScraper.Proxy.Abstract;

public interface IProxyProvider
{
    Task<WebProxy> GetProxyAsync();
}