using System.Net;
using WebReaper.Core.CookieStorage.Abstract;

namespace WebReaper.Core.CookieStorage.Concrete;

/// <inheritdoc />
public class InMemoryCookieStorage : ICookiesStorage
{
    private CookieContainer _cookieContainer = new();

    public Task AddAsync(CookieContainer cookieContainer)
    {
        _cookieContainer = cookieContainer;
        return Task.CompletedTask;
    }

    public Task<CookieContainer> GetAsync()
    {
        return Task.FromResult(_cookieContainer);
    }
}