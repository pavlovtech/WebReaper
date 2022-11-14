using System.Net;

namespace WebReaper.CookiesStorage.Abstract;

public interface ICookiesStorage
{
    Task AddAsync(string siteId, CookieCollection cookieCollection);
    Task GetAsync(string siteId);
}