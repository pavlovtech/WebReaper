using System.Net;
using WebReaper.CookieStorage.Abstract;

namespace WebReaper.CookieStorage.Concrete;

/// <inheritdoc />
public class InMemoryCookieStorage: ICookiesStorage
{
    private CookieContainer _cookieContainer = new();

    public Task AddAsync(CookieContainer cookieContainer)
    {
        _cookieContainer = cookieContainer;
        return Task.CompletedTask;
    }

    public Task<CookieContainer> GetAsync() => Task.FromResult(_cookieContainer);
}