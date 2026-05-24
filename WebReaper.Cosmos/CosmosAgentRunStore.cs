using System.Net;
using Microsoft.Azure.Cosmos;
using WebReaper.Core.Agent.Abstract;
using WebReaper.Domain.Agent;
using WebReaper.Serialization;

namespace WebReaper.Cosmos;

/// <summary>
/// Azure Cosmos DB adapter of the <see cref="IAgentRunStore"/> seam
/// (ADR-0051 §Decision §6). Stores one document per run id in the
/// configured container — <c>{ "id": runId, "snapshotJson": "..." }</c>.
/// The partition key is the run id (the natural partition for per-run
/// access). Upsert is <c>UpsertItemAsync</c>; load is
/// <c>ReadItemAsync</c> with a 404-to-null translation; delete is
/// <c>DeleteItemAsync</c>, also tolerant of 404.
/// <para>
/// Satellite-only (ADR-0009): the <c>Microsoft.Azure.Cosmos</c> dependency
/// stays off the dependency-light core. Suitable for the cloud-native
/// caller who already has Cosmos in their stack.
/// </para>
/// </summary>
public class CosmosAgentRunStore : IAgentRunStore
{
    private readonly Container _container;

    public CosmosAgentRunStore(
        string endpointUrl, string authorizationKey,
        string databaseId, string containerId)
    {
        ArgumentException.ThrowIfNullOrEmpty(endpointUrl);
        ArgumentException.ThrowIfNullOrEmpty(authorizationKey);
        ArgumentException.ThrowIfNullOrEmpty(databaseId);
        ArgumentException.ThrowIfNullOrEmpty(containerId);
        var client = new CosmosClient(endpointUrl, authorizationKey);
        _container = client.GetContainer(databaseId, containerId);
    }

    private sealed record AgentRunDocument(string Id, string SnapshotJson)
    {
        // Cosmos requires the document id property to be named "id".
        public string id => Id;
        public string snapshotJson => SnapshotJson;
    }

    public async ValueTask<AgentRunSnapshot?> LoadAsync(
        string runId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(runId);
        try
        {
            var response = await _container.ReadItemAsync<AgentRunDocument>(
                runId, new PartitionKey(runId), cancellationToken: cancellationToken);
            return response.Resource is null
                ? null
                : WebReaperAgentJson.DeserializeSnapshot(response.Resource.SnapshotJson);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async ValueTask SaveStepAsync(
        string runId, AgentDecision decision, AgentRunSnapshot postState,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(runId);
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(postState);
        var json = WebReaperAgentJson.SerializeSnapshot(postState);
        var doc = new AgentRunDocument(runId, json);
        await _container.UpsertItemAsync(
            doc, new PartitionKey(runId), cancellationToken: cancellationToken);
    }

    public async ValueTask DeleteAsync(string runId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(runId);
        try
        {
            await _container.DeleteItemAsync<AgentRunDocument>(
                runId, new PartitionKey(runId), cancellationToken: cancellationToken);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // already gone — idempotent delete
        }
    }
}
