using WebReaper.DataAccess;

namespace WebReaper.Core.CookieStorage.Concrete;

/// <summary>
/// Source-compatible constructor over the <see cref="CookieStore"/> payload
/// shell backed by an <see cref="InMemoryBlobStore"/> (ADR 0003).
/// </summary>
public class InMemoryCookieStorage : CookieStore
{
    public InMemoryCookieStorage()
        : base(new InMemoryBlobStore(), "cookies")
    {
    }
}
