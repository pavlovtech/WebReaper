using System.Net;
using WebReaper.Core.CookieStorage.Abstract;
using WebReaper.DataAccess;
using WebReaper.Serialization;

namespace WebReaper.Core.CookieStorage.Concrete;

/// <summary>
/// The cookie <strong>payload shell</strong> (ADR 0003). The one home for the
/// <see cref="CookieContainer"/> ↔ <see cref="CookieCollection"/> quirk:
/// <see cref="CookieContainer"/> does not JSON round-trip (internal hash
/// tables), so the shell persists the flat cookie list and rebuilds the
/// container on read. ADR 0008: the grammar is the AOT-clean
/// <see cref="WebReaperJson"/> source-gen one over a flat
/// <see cref="CookieDto"/> — no Newtonsoft. The quirk stays quarantined here
/// and is unreachable from the <see cref="IKeyedBlobStore"/> and any future
/// backend. Absent ⇒ an empty container (what a fresh crawl wants).
/// </summary>
public class CookieStore : ICookiesStorage
{
    private readonly IKeyedBlobStore _store;
    private readonly string _key;

    /// <summary>
    /// Back this cookie store with <paramref name="store"/> at
    /// <paramref name="key"/> (the blob key / path). This is the constructor
    /// the satellite cookie stores (<c>RedisCookieStorage</c>,
    /// <c>MongoDbCookieStorage</c>) chain to.
    /// </summary>
    public CookieStore(IKeyedBlobStore store, string key)
    {
        _store = store;
        _key = key;
    }

    /// <inheritdoc/>
    public Task AddAsync(CookieContainer cookieContainer)
    {
        var dtos = cookieContainer.GetAllCookies()
            .Select(c => new CookieDto(c.Name, c.Value, c.Domain, c.Path))
            .ToArray();

        return _store.PutAsync(_key, WebReaperJson.SerializeCookies(dtos));
    }

    /// <inheritdoc/>
    public async Task<CookieContainer> GetAsync()
    {
        var blob = await _store.GetAsync(_key);
        var container = new CookieContainer();

        if (blob is null)
            return container;

        foreach (var d in WebReaperJson.DeserializeCookies(blob))
            container.Add(new Cookie(d.Name, d.Value, d.Path, d.Domain));

        return container;
    }
}
