using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using WebReaper.Sinks.Abstract;
using WebReaper.Sinks.Models;

namespace WebReaper.Sinks.Concrete;

public class MongoDbSink : IScraperSink
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

        Initialization = InitializeAsync();
    }

    private string ConnectionString { get; }
    private string CollectionName { get; }
    private string DatabaseName { get; }
    private MongoClient Client { get; }
    private ILogger Logger { get; }

    private Task Initialization { get; }

    public bool DataCleanupOnStart { get; set; }

    public async Task EmitAsync(ParsedData entity, CancellationToken cancellationToken = default)
    {
        await Initialization;

        Logger.LogDebug($"Started {nameof(MongoDbSink)}.{nameof(EmitAsync)}");

        var database = Client.GetDatabase(DatabaseName);

        var collection = database.GetCollection<BsonDocument>(CollectionName);

        entity.Data["url"] = entity.Url;

        var document = BsonDocument.Parse(entity.Data.ToString());

        await collection.InsertOneAsync(document, null, cancellationToken);
    }

    private async Task InitializeAsync()
    {
        if (!DataCleanupOnStart)
            return;

        Logger.LogInformation("Started data cleanup");

        var database = Client.GetDatabase(DatabaseName);
        await database.DropCollectionAsync(CollectionName);
    }
}