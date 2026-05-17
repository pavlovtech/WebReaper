using Microsoft.Extensions.Logging;
using WebReaper.Core.CookieStorage.Concrete;

namespace WebReaper.Mongo;

/// <summary>
/// MongoDB cookie storage: the <see cref="CookieStore"/> payload shell (ADR
/// 0003) backed by a <see cref="MongoBlobStore"/>, shipped in the
/// WebReaper.Mongo satellite (ADR 0009). The <paramref name="cookieCollectionId"/>
/// is the blob key (document <c>_id</c>). The <paramref name="logger"/>
/// parameter is vestigial — the payload shell needs none — kept so the
/// constructor matches what <c>WithMongoDbCookieStorage</c> passes. The
/// previous adapter's read called <c>.ToJson()</c> on the <c>FindAsync</c>
/// cursor instead of the document; that bug cannot recur (ADR 0003).
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
