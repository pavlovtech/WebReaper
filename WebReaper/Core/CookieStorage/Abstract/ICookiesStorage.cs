using System.Net;

namespace WebReaper.Core.CookieStorage.Abstract;

public interface ICookiesStorage
{
    Task AddAsync(CookieContainer cookieCollection);
    Task<CookieContainer> GetAsync();
}