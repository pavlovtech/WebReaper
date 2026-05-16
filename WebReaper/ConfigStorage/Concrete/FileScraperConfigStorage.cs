using WebReaper.DataAccess;

namespace WebReaper.ConfigStorage.Concrete;

/// <summary>
/// Source-compatible constructor over the config <see cref="ScraperConfigStore"/>
/// payload shell backed by a <see cref="FileBlobStore"/> (ADR 0003). The
/// <paramref name="fileName"/> is the blob key, so the config is written to
/// exactly that path — unchanged from before.
/// </summary>
public class FileScraperConfigStorage : ScraperConfigStore
{
    public FileScraperConfigStorage(string fileName)
        : base(new FileBlobStore(), fileName)
    {
    }
}
