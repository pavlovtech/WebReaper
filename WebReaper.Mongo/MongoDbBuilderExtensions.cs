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
    /// <summary>
    /// Registers a MongoDB sink: each parsed item is written as a document to
    /// the given collection, over <see cref="ScraperEngineBuilder"/>'s public
    /// <c>AddSink</c> seam.
    /// </summary>
    /// <param name="builder">The scraper engine builder to add the sink to.</param>
    /// <param name="connectionString">MongoDB connection string.</param>
    /// <param name="databaseName">Target database name.</param>
    /// <param name="collectionName">Target collection name.</param>
    /// <param name="dataCleanupOnStart">When <see langword="true"/>, existing data is cleared when the scrape starts.</param>
    /// <param name="logger">Optional logger; defaults to <see cref="NullLogger"/> (a satellite extension cannot reach the builder's private logger).</param>
    /// <returns>The same <see cref="ScraperEngineBuilder"/>, for chaining.</returns>
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

    /// <summary>
    /// Stores the immutable scraper config in MongoDB, over
    /// <see cref="ScraperEngineBuilder"/>'s public <c>WithConfigStorage</c>
    /// seam — so multiple workers can share crawl configuration.
    /// </summary>
    /// <param name="builder">The scraper engine builder.</param>
    /// <param name="connectionString">MongoDB connection string.</param>
    /// <param name="databaseName">Database holding the config document.</param>
    /// <param name="collectionName">Collection holding the config document.</param>
    /// <param name="configId">Id of the config document to read/write.</param>
    /// <param name="logger">Optional logger; defaults to <see cref="NullLogger"/>.</param>
    /// <returns>The same <see cref="ScraperEngineBuilder"/>, for chaining.</returns>
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

    /// <summary>
    /// Stores session cookies in MongoDB, over
    /// <see cref="ScraperEngineBuilder"/>'s public <c>WithCookieStorage</c>
    /// seam — so multiple workers can share an authenticated session.
    /// </summary>
    /// <param name="builder">The scraper engine builder.</param>
    /// <param name="connectionString">MongoDB connection string.</param>
    /// <param name="databaseName">Database holding the cookie document.</param>
    /// <param name="collectionName">Collection holding the cookie document.</param>
    /// <param name="cookieCollectionId">Id of the cookie document to read/write.</param>
    /// <param name="logger">Optional logger; defaults to <see cref="NullLogger"/>.</param>
    /// <returns>The same <see cref="ScraperEngineBuilder"/>, for chaining.</returns>
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
