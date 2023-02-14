using System.Net;

namespace ExoScraper.CookieStorage.Abstract;

public interface ICookiesStorage
{
    Task AddAsync(CookieContainer cookieCollection);
    Task<CookieContainer> GetAsync();
}