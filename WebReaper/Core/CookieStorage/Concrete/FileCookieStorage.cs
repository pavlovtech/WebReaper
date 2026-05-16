using Microsoft.Extensions.Logging;
using WebReaper.DataAccess;

namespace WebReaper.Core.CookieStorage.Concrete;

/// <summary>
/// Source-compatible constructor over the <see cref="CookieStore"/> payload
/// shell backed by a <see cref="FileBlobStore"/> (ADR 0003). The
/// <paramref name="fileName"/> is the blob key (the file path). The
/// <paramref name="logger"/> parameter is retained for binary/source
/// compatibility; it is no longer used here.
/// </summary>
public class FileCookieStorage : CookieStore
{
    public FileCookieStorage(string fileName, ILogger logger)
        : base(new FileBlobStore(), fileName)
    {
    }
}
