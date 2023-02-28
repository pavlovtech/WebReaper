using System.Net;

namespace WebReaper.Proxy.Abstract;

public interface IProxyProvider
{
    Task<WebProxy> GetProxyAsync();
}