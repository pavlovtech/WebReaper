using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WebReaper.Builders;

namespace WebReaper.Mongo;

/// <summary>
/// ADR-0009: the MongoDB builder sugar lives here, in the WebReaper.Mongo
/// satellite, over <see cref="ScraperEngineBuilder"/>'s public registration
/// seam (<c>AddSink</c> / <c>WithConfigStorage</c> / <c>WithCookieStorage</c>)
/// — not in core. Core no longer references MongoDB.Driver. A satellite
/// extension cannot reach the builder's private logger, so the logger is an
/// explicit optional argument (defaulting to <see cref="NullLogger"/>) rather
/// than the builder's.
/// </summary>
public static class MongoDbBuilderExtensions
{
    public static ScraperEngineBuilder WriteToMongoDb(
        this ScraperEngineBuilder builder,
        string connectionString,
        string databaseName,
        string collectionName,
        bool dataCleanupOnStart,
        ILogger? logger = null)
    {
        return builder.AddSink(new MongoDbSink(
            connectionString,
            databaseName,
            collectionName,
            dataCleanupOnStart,
            logger ?? NullLogger.Instance));
    }

    public static ScraperEngineBuilder WithMongoDbConfigStorage(
        this ScraperEngineBuilder builder,
        string connectionString,
        string databaseName,
        string collectionName,
        string configId,
        ILogger? logger = null)
    {
        return builder.WithConfigStorage(new MongoDbScraperConfigStorage(
            connectionString,
            databaseName,
            collectionName,
            configId,
            logger ?? NullLogger.Instance));
    }

    public static ScraperEngineBuilder WithMongoDbCookieStorage(
        this ScraperEngineBuilder builder,
        string connectionString,
        string databaseName,
        string collectionName,
        string cookieCollectionId,
        ILogger? logger = null)
    {
        return builder.WithCookieStorage(new MongoDbCookieStorage(
            connectionString,
            databaseName,
            collectionName,
            cookieCollectionId,
            logger ?? NullLogger.Instance));
    }
}
