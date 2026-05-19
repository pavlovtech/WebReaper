using WebReaper.DataAccess;

namespace WebReaper.ConfigStorage.Concrete;

/// <summary>
/// Source-compatible constructor over the config <see cref="ScraperConfigStore"/>
/// payload shell backed by an <see cref="InMemoryBlobStore"/> (ADR 0003).
/// </summary>
public class InMemoryScraperConfigStorage : ScraperConfigStore
{
    /// <summary>An in-process config store (the default; also the in-memory
    /// building block the ADR-0009 DIY-distributed pattern wires by hand).</summary>
    public InMemoryScraperConfigStorage()
        : base(new InMemoryBlobStore(), "config")
    {
    }
}
