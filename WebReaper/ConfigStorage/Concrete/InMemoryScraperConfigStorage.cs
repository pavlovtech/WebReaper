using WebReaper.DataAccess;

namespace WebReaper.ConfigStorage.Concrete;

/// <summary>
/// Source-compatible constructor over the config <see cref="ScraperConfigStore"/>
/// payload shell backed by an <see cref="InMemoryBlobStore"/> (ADR 0003).
/// </summary>
public class InMemoryScraperConfigStorage : ScraperConfigStore
{
    public InMemoryScraperConfigStorage()
        : base(new InMemoryBlobStore(), "config")
    {
    }
}
