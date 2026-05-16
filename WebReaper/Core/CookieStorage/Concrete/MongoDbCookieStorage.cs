using Microsoft.Extensions.Logging;
using WebReaper.DataAccess;

namespace WebReaper.Core.CookieStorage.Concrete;

/// <summary>
/// Source-compatible constructor over the <see cref="CookieStore"/> payload
/// shell backed by a <see cref="MongoBlobStore"/> (ADR 0003). The
/// <paramref name="cookieCollectionId"/> is the blob key (document <c>_id</c>).
/// The <paramref name="logger"/> parameter is retained for binary/source
/// compatibility; it is no longer used here. Replaces the previous adapter
/// whose read called <c>.ToJson()</c> on the <c>FindAsync</c> cursor instead
/// of the document — that bug cannot recur (ADR 0003).
/// </summary>
public class MongoDbCookieStorage : CookieStore
{
    public MongoDbCookieStorage(
        string connectionString,
        string databaseName,
        string collectionName,
        string cookieCollectionId,
        ILogger logger)
        : base(new MongoBlobStore(connectionString, databaseName, collectionName), cookieCollectionId)
    {
    }
}
