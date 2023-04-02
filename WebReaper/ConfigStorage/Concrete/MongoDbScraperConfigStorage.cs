using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using Newtonsoft.Json;
using WebReaper.ConfigStorage.Abstract;
using WebReaper.Domain;

namespace WebReaper.ConfigStorage.Concrete;

public class MongoDbScraperConfigStorage : IScraperConfigStorage
{
    private readonly string _configId;

    public MongoDbScraperConfigStorage(
        string connectionString,
        string databaseName,
        string collectionName,
        string configId,
        ILogger logger)
    {
        _configId = configId;
        ConnectionString = connectionString;
        CollectionName = collectionName;
        DatabaseName = databaseName;
        Client = new MongoClient(ConnectionString);
        Logger = logger;
    }

    private string ConnectionString { get; }
    private string CollectionName { get; }
    private string DatabaseName { get; }
    private MongoClient Client { get; }
    private ILogger Logger { get; }

    public async Task CreateConfigAsync(ScraperConfig config)
    {
        var database = Client.GetDatabase(DatabaseName);
        var collection = database.GetCollection<BsonDocument>(CollectionName);
        var doc = config.ToBsonDocument();
        doc["id"] = _configId;
        await collection.InsertOneAsync(doc);
    }

    public async Task<ScraperConfig> GetConfigAsync()
    {
        var database = Client.GetDatabase(DatabaseName);
        var collection = database.GetCollection<BsonDocument>(CollectionName);
        var configCursor = await collection.FindAsync(c => c["id"] == _configId);

        var config = await configCursor.FirstOrDefaultAsync();

        if (config == null) return null;

        config.Remove("id");
        config.Remove("_id");

        var json = config.ToJson();

        var result = JsonConvert.DeserializeObject<ScraperConfig>(json);

        return result;
    }
}