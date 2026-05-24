using StackExchange.Redis;
using WebReaper.Core.Agent.Abstract;
using WebReaper.Domain.Agent;
using WebReaper.Serialization;

namespace WebReaper.Redis;

/// <summary>
/// Redis adapter of the <see cref="IAgentRunStore"/> seam (ADR-0051 §Decision
/// §6). Stores the JSON-serialized <see cref="AgentRunSnapshot"/> as a
/// Redis STRING under the key prefix supplied at construction —
/// <c>{keyPrefix}:{runId}</c>. Atomicity per-step is the natural single-key
/// <c>SET</c>; the snapshot is whole-document overwrite, not partial.
/// <para>
/// Shares the satellite-wide <see cref="RedisConnectionPool"/> (ADR-0005,
/// one <c>ConnectionMultiplexer</c> per connection string).
/// </para>
/// </summary>
public class RedisAgentRunStore : IAgentRunStore
{
    private readonly IDatabase _db;
    private readonly string _keyPrefix;

    public RedisAgentRunStore(string connectionString, string keyPrefix = "webreaper:agent:run")
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionString);
        ArgumentException.ThrowIfNullOrEmpty(keyPrefix);
        _db = RedisConnectionPool.GetDatabase(connectionString);
        _keyPrefix = keyPrefix;
    }

    private string KeyFor(string runId) => $"{_keyPrefix}:{runId}";

    public async ValueTask<AgentRunSnapshot?> LoadAsync(
        string runId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(runId);
        var raw = await _db.StringGetAsync(KeyFor(runId));
        if (raw.IsNullOrEmpty) return null;
        return WebReaperAgentJson.DeserializeSnapshot(raw!);
    }

    public async ValueTask SaveStepAsync(
        string runId, AgentDecision decision, AgentRunSnapshot postState,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(runId);
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(postState);
        var json = WebReaperAgentJson.SerializeSnapshot(postState);
        await _db.StringSetAsync(KeyFor(runId), json);
    }

    public async ValueTask DeleteAsync(string runId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(runId);
        await _db.KeyDeleteAsync(KeyFor(runId));
    }
}
