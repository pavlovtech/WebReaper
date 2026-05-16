using Microsoft.Extensions.Logging;
using WebReaper.DataAccess;

namespace WebReaper.ConfigStorage.Concrete;

/// <summary>
/// Source-compatible constructor over the config <see cref="ScraperConfigStore"/>
/// payload shell backed by a <see cref="MongoBlobStore"/> (ADR 0003). The
/// <paramref name="configId"/> is the blob key (document <c>_id</c>). The
/// <paramref name="logger"/> parameter is retained for binary/source
/// compatibility; it is no longer used here.
/// </summary>
public class MongoDbScraperConfigStorage : ScraperConfigStore
{
    public MongoDbScraperConfigStorage(
        string connectionString,
        string databaseName,
        string collectionName,
        string configId,
        ILogger logger)
        : base(new MongoBlobStore(connectionString, databaseName, collectionName), configId)
    {
    }
}
