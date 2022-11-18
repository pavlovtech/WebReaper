using System.Net;

namespace Exoscan.Proxy.Abstract;

public interface IProxyProvider
{
    Task<WebProxy> GetProxyAsync();
}