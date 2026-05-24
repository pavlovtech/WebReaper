using MongoDB.Bson;
using MongoDB.Driver;
using WebReaper.Core.Agent.Abstract;
using WebReaper.Domain.Agent;
using WebReaper.Serialization;

namespace WebReaper.Mongo;

/// <summary>
/// MongoDB adapter of the <see cref="IAgentRunStore"/> seam (ADR-0051
/// §Decision §6). Stores a document per run id —
/// <c>{ _id: runId, snapshotJson: "..." }</c> — in the configured
/// collection. The snapshot is opaque JSON; WebReaper never queries inside
/// it, mirroring the <see cref="MongoBlobStore"/> pattern (ADR-0003: the
/// historical queryable-BSON projection was never load-bearing).
/// </summary>
public class MongoAgentRunStore : IAgentRunStore
{
    private const string SnapshotField = "snapshotJson";

    private readonly IMongoCollection<BsonDocument> _collection;

    public MongoAgentRunStore(string connectionString, string databaseName, string collectionName = "agent_runs")
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionString);
        ArgumentException.ThrowIfNullOrEmpty(databaseName);
        ArgumentException.ThrowIfNullOrEmpty(collectionName);
        _collection = new MongoClient(connectionString)
            .GetDatabase(databaseName)
            .GetCollection<BsonDocument>(collectionName);
    }

    public async ValueTask<AgentRunSnapshot?> LoadAsync(
        string runId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(runId);
        var filter = Builders<BsonDocument>.Filter.Eq("_id", runId);
        var doc = await _collection.Find(filter).FirstOrDefaultAsync(cancellationToken);
        if (doc is null || !doc.TryGetValue(SnapshotField, out var raw)) return null;
        return WebReaperAgentJson.DeserializeSnapshot(raw.AsString);
    }

    public async ValueTask SaveStepAsync(
        string runId, AgentDecision decision, AgentRunSnapshot postState,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(runId);
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(postState);
        var json = WebReaperAgentJson.SerializeSnapshot(postState);
        var doc = new BsonDocument { { "_id", runId }, { SnapshotField, json } };
        await _collection.ReplaceOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", runId),
            doc,
            new ReplaceOptions { IsUpsert = true },
            cancellationToken);
    }

    public async ValueTask DeleteAsync(string runId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(runId);
        await _collection.DeleteOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", runId), cancellationToken);
    }
}
