using System.Net;
using WebReaper.CookieStorage.Abstract;

namespace WebReaper.CookieStorage.Concrete;

public class InMemoryCookieStorage: ICookiesStorage
{
    private CookieContainer _cookieContainer = new();

    public async Task AddAsync(CookieContainer cookieContainer) =>
        _cookieContainer = cookieContainer;

    public async Task<CookieContainer> GetAsync() => _cookieContainer;
}