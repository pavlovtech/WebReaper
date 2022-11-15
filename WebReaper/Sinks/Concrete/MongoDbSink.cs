using WebReaper.Sinks.Abstract;
using MongoDB.Bson;
using MongoDB.Driver;
using Microsoft.Extensions.Logging;
using WebReaper.Sinks.Models;

namespace WebReaper.Sinks.Concrete;

public class MongoDbSink : IScraperSink
{
    private string ConnectionString { get; }
    private string CollectionName { get; }
    private string DatabaseName { get; }
    private MongoClient Client { get; }
    private ILogger Logger { get; }

    public MongoDbSink(string connectionString, string databaseName, string collectionName, ILogger logger)
    {
        ConnectionString = connectionString;
        CollectionName = collectionName;
        DatabaseName = databaseName;
        Client = new MongoClient(ConnectionString);
        Logger = logger;
    }
    
    public async Task EmitAsync(ParsedData entity, CancellationToken cancellationToken = default)
    {
        Logger.LogDebug($"Started {nameof(MongoDbSink)}.{nameof(EmitAsync)}");
            
        var database = Client.GetDatabase(DatabaseName);

        var collection = database.GetCollection<BsonDocument>(CollectionName);
        
        entity.Data["url"] = entity.Url;

        var document = BsonDocument.Parse(entity.Data.ToString());

        await collection.InsertOneAsync(document, null, cancellationToken);
    }
}