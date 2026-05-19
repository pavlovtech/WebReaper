using WebReaper.DataAccess;

namespace WebReaper.ConfigStorage.Concrete;

/// <summary>
/// Source-compatible constructor over the config <see cref="ScraperConfigStore"/>
/// payload shell backed by a <see cref="FileBlobStore"/> (ADR 0003). The
/// <c>fileName</c> is the blob key, so the config is written to
/// exactly that path — unchanged from before.
/// </summary>
internal class FileScraperConfigStorage : ScraperConfigStore
{
    public FileScraperConfigStorage(string fileName)
        : base(new FileBlobStore(), fileName)
    {
    }
}
