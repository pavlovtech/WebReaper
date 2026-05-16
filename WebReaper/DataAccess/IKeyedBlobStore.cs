namespace WebReaper.DataAccess;

/// <summary>
/// Backend-agnostic persistence seam: stores and fetches one opaque UTF-8
/// string under a caller-supplied key. <see cref="PutAsync"/> is
/// upsert/replace (last write wins, never append). A <c>null</c> result from
/// <see cref="GetAsync"/> means the key is absent — absence is not an error at
/// this seam; what absence <em>means</em> is decided by the payload shell
/// above it (see <see cref="WebReaper.ConfigStorage.Concrete.ScraperConfigStore"/>,
/// <see cref="WebReaper.Core.CookieStorage.Concrete.CookieStore"/>).
/// The key is opaque to the store and scoped to the backend the adapter talks
/// to (a file path, a Redis key, a document id). The store never knows which
/// payload it holds. See docs/adr/0003-keyed-blob-store-and-payload-shells.md.
/// </summary>
public interface IKeyedBlobStore
{
    /// <summary>Persist <paramref name="value"/> under <paramref name="key"/>, replacing any existing value.</summary>
    Task PutAsync(string key, string value);

    /// <summary>Fetch the value stored under <paramref name="key"/>, or <c>null</c> if the key is absent.</summary>
    Task<string?> GetAsync(string key);
}
