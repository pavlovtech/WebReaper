using System.Net;

namespace WebReaper.CookieStorage.Abstract;

public interface ICookiesStorage
{
    Task AddAsync(CookieContainer cookieCollection);
    Task<CookieContainer> GetAsync();
}