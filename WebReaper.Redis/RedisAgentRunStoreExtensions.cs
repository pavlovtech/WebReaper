using WebReaper.Builders;

namespace WebReaper.Redis;

/// <summary>
/// <see cref="AgentEngineBuilder"/> extensions for the
/// <see cref="RedisAgentRunStore"/> (ADR-0051 §Decision §6) — registers
/// the Redis-backed durable agent-run snapshot store on the agent builder.
/// </summary>
public static class RedisAgentRunStoreExtensions
{
    /// <summary>
    /// Persist agent run snapshots to Redis. Each run is one STRING value
    /// under <c>{keyPrefix}:{runId}</c> (default prefix
    /// <c>webreaper:agent:run</c>).
    /// </summary>
    public static AgentEngineBuilder WithRedisAgentRunStore(
        this AgentEngineBuilder builder,
        string connectionString,
        string keyPrefix = "webreaper:agent:run")
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithRunStore(new RedisAgentRunStore(connectionString, keyPrefix));
    }
}
