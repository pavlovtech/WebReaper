using System.Net;
using Newtonsoft.Json;
using WebReaper.Core.CookieStorage.Abstract;
using WebReaper.DataAccess;

namespace WebReaper.Core.CookieStorage.Concrete;

/// <summary>
/// The cookie <strong>payload shell</strong> (ADR 0003). The one home for the
/// <see cref="CookieContainer"/> ↔ <see cref="CookieCollection"/> quirk:
/// <see cref="CookieContainer"/> does not JSON round-trip (internal hash
/// tables), so the shell persists the flat <see cref="CookieCollection"/> and
/// rebuilds the container on read. This quirk is quarantined here and is
/// unreachable from the <see cref="IKeyedBlobStore"/> and any future backend.
/// Absent ⇒ an empty container (what a fresh crawl wants).
/// </summary>
public class CookieStore : ICookiesStorage
{
    private readonly IKeyedBlobStore _store;
    private readonly string _key;

    public CookieStore(IKeyedBlobStore store, string key)
    {
        _store = store;
        _key = key;
    }

    public Task AddAsync(CookieContainer cookieContainer)
        => _store.PutAsync(_key, JsonConvert.SerializeObject(cookieContainer.GetAllCookies()));

    public async Task<CookieContainer> GetAsync()
    {
        var blob = await _store.GetAsync(_key);
        var container = new CookieContainer();

        if (blob is null)
            return container;

        var cookies = JsonConvert.DeserializeObject<CookieCollection>(blob);

        if (cookies != null)
            container.Add(cookies);

        return container;
    }
}
