using System.Net;

namespace Exoscan.CookieStorage.Abstract;

public interface ICookiesStorage
{
    Task AddAsync(CookieContainer cookieCollection);
    Task<CookieContainer> GetAsync();
}