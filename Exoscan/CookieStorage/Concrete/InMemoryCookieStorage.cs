using System.Net;
using Exoscan.CookieStorage.Abstract;

namespace Exoscan.CookieStorage.Concrete;

public class InMemoryCookieStorage: ICookiesStorage
{
    private CookieContainer _cookieContainer = new();

    public async Task AddAsync(CookieContainer cookieContainer) =>
        _cookieContainer = cookieContainer;

    public Task<CookieContainer> GetAsync() => Task.FromResult(_cookieContainer);
}