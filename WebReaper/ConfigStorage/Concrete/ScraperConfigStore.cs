using WebReaper.ConfigStorage.Abstract;
using WebReaper.DataAccess;
using WebReaper.Domain;
using WebReaper.Exceptions;
using WebReaper.Serialization;

namespace WebReaper.ConfigStorage.Concrete;

/// <summary>
/// The config <strong>payload shell</strong> (ADR 0003). The one home for
/// scraper-config serialization and the meaning of <em>absent</em>. Per ADR
/// 0008 it now owns a <see cref="WebReaperJson"/> System.Text.Json source-gen
/// grammar instead of Newtonsoft <c>TypeNameHandling.Auto</c> — applied (as
/// before) <em>symmetrically</em>, so the polymorphic <c>PageAction.Parameters</c>
/// (<c>object[]</c>) and the <c>ImmutableQueue&lt;LinkPathSelector&gt;</c>
/// chain round-trip through every backend (the dedicated converters, not
/// reflection-by-typename). The ADR-0003 structural result is unchanged:
/// storage is delegated to one <see cref="IKeyedBlobStore"/>; the store never
/// sees a config; <em>absent</em> still means a typed not-found.
/// </summary>
public class ScraperConfigStore : IScraperConfigStorage
{
    private readonly IKeyedBlobStore _store;
    private readonly string _key;

    public ScraperConfigStore(IKeyedBlobStore store, string key)
    {
        _store = store;
        _key = key;
    }

    public Task CreateConfigAsync(ScraperConfig config)
        => _store.PutAsync(_key, WebReaperJson.SerializeConfig(config));

    public async Task<ScraperConfig> GetConfigAsync()
    {
        var blob = await _store.GetAsync(_key);

        if (blob is null)
            throw new ConfigNotFoundException(_key);

        return WebReaperJson.DeserializeConfig(blob);
    }
}
