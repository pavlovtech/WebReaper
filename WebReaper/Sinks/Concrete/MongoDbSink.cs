using Newtonsoft.Json.Linq;
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
    
    public async Task EmitAsync(ParsedData parsedData, CancellationToken cancellationToken = default)
    {
        Logger.LogDebug($"Started {nameof(MongoDbSink)}.{nameof(EmitAsync)}");
            
        var database = Client.GetDatabase(DatabaseName);

        var collection = database.GetCollection<BsonDocument>(CollectionName);
        
        parsedData.Data["url"] = parsedData.Url;

        var document = BsonDocument.Parse(parsedData.Data.ToString());

        await collection.InsertOneAsync(document, null, cancellationToken);
    }
}