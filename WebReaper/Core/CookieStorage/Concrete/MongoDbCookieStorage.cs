using System.Net;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using Newtonsoft.Json;
using WebReaper.Core.CookieStorage.Abstract;

namespace WebReaper.Core.CookieStorage.Concrete;

public class MongoDbCookieStorage : ICookiesStorage
{
    private readonly string _cookieCollectionId;

    public MongoDbCookieStorage(string connectionString, string databaseName, string collectionName,
        string cookieCollectionId, ILogger logger)
    {
        _cookieCollectionId = cookieCollectionId;
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

    public async Task AddAsync(CookieContainer cookieContainer)
    {
        var database = Client.GetDatabase(DatabaseName);
        var collection = database.GetCollection<BsonDocument>(CollectionName);
        var doc = cookieContainer.ToBsonDocument();
        doc["id"] = _cookieCollectionId;
        await collection.InsertOneAsync(doc);
    }

    public async Task<CookieContainer> GetAsync()
    {
        var database = Client.GetDatabase(DatabaseName);
        var collection = database.GetCollection<BsonDocument>(CollectionName);
        var config = await collection.FindAsync(c => c["id"] == _cookieCollectionId);

        var json = config.ToJson();

        var result = JsonConvert.DeserializeObject<CookieContainer>(json);

        return result;
    }
}