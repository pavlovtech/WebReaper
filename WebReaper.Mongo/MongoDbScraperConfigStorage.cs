using Microsoft.Extensions.Logging;
using WebReaper.ConfigStorage.Concrete;

namespace WebReaper.Mongo;

/// <summary>
/// MongoDB scraper-config storage: the config <see cref="ScraperConfigStore"/>
/// payload shell (ADR 0003) backed by a <see cref="MongoBlobStore"/>, shipped
/// in the WebReaper.Mongo satellite (ADR 0009). The <paramref name="configId"/>
/// is the blob key (document <c>_id</c>). The <paramref name="logger"/>
/// parameter is vestigial — the payload shell needs none — kept so the
/// constructor matches what <c>WithMongoDbConfigStorage</c> passes.
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
