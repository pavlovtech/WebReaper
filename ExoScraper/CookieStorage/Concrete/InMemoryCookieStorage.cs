using System.Net;
using ExoScraper.CookieStorage.Abstract;

namespace ExoScraper.CookieStorage.Concrete;

public class InMemoryCookieStorage: ICookiesStorage
{
    private CookieContainer _cookieContainer = new();

    public async Task AddAsync(CookieContainer cookieContainer) =>
        _cookieContainer = cookieContainer;

    public Task<CookieContainer> GetAsync() => Task.FromResult(_cookieContainer);
}