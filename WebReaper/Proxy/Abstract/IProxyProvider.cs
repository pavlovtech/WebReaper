using System.Net;

namespace WebReaper.Proxy.Abstract
{
    internal interface IProxyProvider
    {
        WebProxy GetProxy();
    }
}
