using System.Collections.Concurrent;
using WebReaper.Core.Agent.Abstract;
using WebReaper.Domain.Agent;

namespace WebReaper.Core.Agent.Concrete;

/// <summary>
/// The in-memory default <see cref="IAgentRunStore"/> (ADR-0051 §Decision §6).
/// One <see cref="ConcurrentDictionary{TKey,TValue}"/> keyed by run id;
/// snapshots survive process lifetime only.
/// <para>
/// Suitable for the firecrawl-shaped one-liner caller where the run is
/// short-lived and awaited — nothing to resume across processes. Distributed
/// callers, or callers that want resumability across <c>kubectl rollout
/// restart</c>, swap in one of the durable adapters (File in core; Redis /
/// Mongo / Sqlite / Cosmos in the satellites — ADR-0009 pattern).
/// </para>
/// </summary>
internal sealed class InMemoryAgentRunStore : IAgentRunStore
{
    private readonly ConcurrentDictionary<string, AgentRunSnapshot> _snapshots = new();

    /// <inheritdoc/>
    public ValueTask<AgentRunSnapshot?> LoadAsync(
        string runId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(runId);
        return ValueTask.FromResult(_snapshots.TryGetValue(runId, out var snapshot) ? snapshot : null);
    }

    /// <inheritdoc/>
    public ValueTask SaveStepAsync(
        string runId,
        AgentDecision decision,
        AgentRunSnapshot postState,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(runId);
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(postState);
        _snapshots[runId] = postState;
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask DeleteAsync(
        string runId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(runId);
        _snapshots.TryRemove(runId, out _);
        return ValueTask.CompletedTask;
    }
}
