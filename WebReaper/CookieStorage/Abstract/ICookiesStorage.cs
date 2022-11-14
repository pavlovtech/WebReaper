using System.Net;

namespace WebReaper.CookieStorage.Abstract;

public interface ICookiesStorage
{
    Task AddAsync(CookieContainer cookieCollection, TimeSpan timeToLive);
    Task<CookieContainer> GetAsync();
}