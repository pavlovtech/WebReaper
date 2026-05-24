using WebReaper.Builders;

namespace WebReaper.Sqlite;

/// <summary>
/// <see cref="AgentEngineBuilder"/> extensions for the
/// <see cref="SqliteAgentRunStore"/> (ADR-0051 §Decision §6) — registers
/// the embedded-SQLite durable agent-run snapshot store on the agent builder.
/// </summary>
public static class SqliteAgentRunStoreExtensions
{
    /// <summary>
    /// Persist agent run snapshots to a local SQLite database. The store
    /// creates an <c>agent_runs</c> table on construction if it doesn't
    /// exist yet.
    /// </summary>
    public static AgentEngineBuilder WithSqliteAgentRunStore(
        this AgentEngineBuilder builder,
        string databasePath)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithRunStore(new SqliteAgentRunStore(databasePath));
    }
}
