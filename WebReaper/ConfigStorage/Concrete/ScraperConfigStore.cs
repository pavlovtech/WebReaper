using Newtonsoft.Json;
using WebReaper.ConfigStorage.Abstract;
using WebReaper.DataAccess;
using WebReaper.Domain;
using WebReaper.Exceptions;

namespace WebReaper.ConfigStorage.Concrete;

/// <summary>
/// The config <strong>payload shell</strong> (ADR 0003). The one home for
/// scraper-config serialization and the meaning of <em>absent</em>. Owns
/// <see cref="TypeNameHandling.Auto"/> — applied <em>symmetrically</em> to
/// serialize and deserialize, so the polymorphic <c>PageAction.Parameters</c>
/// (<c>object[]</c>) and the <c>ImmutableQueue&lt;LinkPathSelector&gt;</c>
/// chain round-trip through every backend (Redis was silently lossy with
/// <c>TypeNameHandling.None</c>; the file adapter serialized with
/// <c>Auto</c> but deserialized with defaults). Delegates storage to one
/// <see cref="IKeyedBlobStore"/>; the store never sees a config.
/// </summary>
public class ScraperConfigStore : IScraperConfigStorage
{
    private static readonly JsonSerializerSettings Settings = new()
    {
        TypeNameHandling = TypeNameHandling.Auto,
        NullValueHandling = NullValueHandling.Ignore,
        TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple
    };

    private readonly IKeyedBlobStore _store;
    private readonly string _key;

    public ScraperConfigStore(IKeyedBlobStore store, string key)
    {
        _store = store;
        _key = key;
    }

    public Task CreateConfigAsync(ScraperConfig config)
        => _store.PutAsync(_key, JsonConvert.SerializeObject(config, Formatting.Indented, Settings));

    public async Task<ScraperConfig> GetConfigAsync()
    {
        var blob = await _store.GetAsync(_key);

        if (blob is null)
            throw new ConfigNotFoundException(_key);

        return JsonConvert.DeserializeObject<ScraperConfig>(blob, Settings)
               ?? throw new ConfigNotFoundException(_key);
    }
}
