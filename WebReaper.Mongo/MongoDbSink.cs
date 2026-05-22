using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using WebReaper.Infra.Abstract;
using WebReaper.Sinks.Abstract;
using WebReaper.Sinks.Models;

namespace WebReaper.Mongo;

public class MongoDbSink : IScraperSink, IAsyncInitializable
{
    public MongoDbSink(
        string connectionString,
        string databaseName,
        string collectionName,
        bool dataCleanupOnStart,
        ILogger logger)
    {
        ConnectionString = connectionString;
        CollectionName = collectionName;
        DataCleanupOnStart = dataCleanupOnStart;
        DatabaseName = databaseName;
        Client = new MongoClient(ConnectionString);
        Logger = logger;

        _initialization = new Lazy<Task>(InitializeCoreAsync);
    }

    private string ConnectionString { get; }
    private string CollectionName { get; }
    private string DatabaseName { get; }
    private MongoClient Client { get; }
    private ILogger Logger { get; }

    private readonly Lazy<Task> _initialization;

    public bool DataCleanupOnStart { get; set; }

    public async Task EmitAsync(ParsedData entity, CancellationToken cancellationToken = default)
    {
        Logger.LogDebug($"Started {nameof(MongoDbSink)}.{nameof(EmitAsync)}");

        var database = Client.GetDatabase(DatabaseName);

        var collection = database.GetCollection<BsonDocument>(CollectionName);

        // ADR-0031: the page URL is already folded into entity.Data by
        // ParsedData's construction — no per-sink merge.
        // ADR 0008: entity.Data is a System.Text.Json JsonObject; ToJsonString
        // is the compact, valid-JSON BsonDocument.Parse expects (no Newtonsoft).
        var document = BsonDocument.Parse(entity.Data.ToJsonString());

        await collection.InsertOneAsync(document, null, cancellationToken);
    }

    // ADR-0033: idempotent async warm-up, driven once before the crawl.
    public Task InitializeAsync() => _initialization.Value;

    private async Task InitializeCoreAsync()
    {
        if (!DataCleanupOnStart)
            return;

        Logger.LogInformation("Started data cleanup");

        var database = Client.GetDatabase(DatabaseName);
        await database.DropCollectionAsync(CollectionName);
    }
}