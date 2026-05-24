using WebReaper.Builders;

namespace WebReaper.Cosmos;

/// <summary>
/// <see cref="AgentEngineBuilder"/> extensions for the
/// <see cref="CosmosAgentRunStore"/> (ADR-0051 §Decision §6) — registers the
/// Azure Cosmos DB-backed durable agent-run snapshot store on the agent
/// builder.
/// </summary>
public static class CosmosAgentRunStoreExtensions
{
    /// <summary>
    /// Persist agent run snapshots to Azure Cosmos DB. Each run is one
    /// document keyed and partitioned by <c>runId</c> in the configured
    /// container.
    /// </summary>
    public static AgentEngineBuilder WithCosmosAgentRunStore(
        this AgentEngineBuilder builder,
        string endpointUrl,
        string authorizationKey,
        string databaseId,
        string containerId)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithRunStore(new CosmosAgentRunStore(endpointUrl, authorizationKey, databaseId, containerId));
    }
}
