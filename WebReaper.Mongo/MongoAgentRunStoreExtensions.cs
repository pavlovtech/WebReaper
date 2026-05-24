using WebReaper.Builders;

namespace WebReaper.Mongo;

/// <summary>
/// <see cref="AgentEngineBuilder"/> extensions for the
/// <see cref="MongoAgentRunStore"/> (ADR-0051 §Decision §6) — registers the
/// MongoDB-backed durable agent-run snapshot store on the agent builder.
/// </summary>
public static class MongoAgentRunStoreExtensions
{
    /// <summary>
    /// Persist agent run snapshots to MongoDB. Each run is one document
    /// (<c>{ _id: runId, snapshotJson: "..." }</c>) in
    /// <paramref name="collectionName"/> (default <c>agent_runs</c>).
    /// </summary>
    public static AgentEngineBuilder WithMongoAgentRunStore(
        this AgentEngineBuilder builder,
        string connectionString,
        string databaseName,
        string collectionName = "agent_runs")
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithRunStore(new MongoAgentRunStore(connectionString, databaseName, collectionName));
    }
}
