using MongoDB.Bson;
using MongoDB.Driver;

namespace WebReaper.DataAccess;

/// <summary>
/// MongoDB-backed <see cref="IKeyedBlobStore"/>. Stores an opaque
/// <c>{ _id: key, blob: value }</c> document — WebReaper only ever fetches a
/// whole payload by key and never queries inside it, so the historical
/// queryable-BSON projection was never load-bearing (ADR 0003). The upsert
/// makes the old <c>InsertOneAsync</c> + <c>FirstOrDefault</c>
/// append-and-read-oldest bug unrepresentable.
/// </summary>
public class MongoBlobStore : IKeyedBlobStore
{
    private const string BlobField = "blob";

    private readonly IMongoCollection<BsonDocument> _collection;

    public MongoBlobStore(string connectionString, string databaseName, string collectionName)
    {
        _collection = new MongoClient(connectionString)
            .GetDatabase(databaseName)
            .GetCollection<BsonDocument>(collectionName);
    }

    public Task PutAsync(string key, string value)
        => _collection.ReplaceOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", key),
            new BsonDocument { { "_id", key }, { BlobField, value } },
            new ReplaceOptions { IsUpsert = true });

    public async Task<string?> GetAsync(string key)
    {
        var doc = await _collection
            .Find(Builders<BsonDocument>.Filter.Eq("_id", key))
            .FirstOrDefaultAsync();

        return doc is null ? null : doc[BlobField].AsString;
    }
}
