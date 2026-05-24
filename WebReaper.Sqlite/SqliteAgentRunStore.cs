using Microsoft.Data.Sqlite;
using WebReaper.Core.Agent.Abstract;
using WebReaper.Domain.Agent;
using WebReaper.Serialization;

namespace WebReaper.Sqlite;

/// <summary>
/// Embedded-SQLite adapter of the <see cref="IAgentRunStore"/> seam
/// (ADR-0051 §Decision §6). One table — <c>agent_runs(run_id TEXT PRIMARY
/// KEY, snapshot_json TEXT NOT NULL)</c> — written transactionally on every
/// <see cref="SaveStepAsync"/>. Resume queries are <c>SELECT snapshot_json
/// FROM agent_runs WHERE run_id = ?</c>; no row → <see cref="LoadAsync"/>
/// returns null.
/// <para>
/// Satellite-only (ADR-0009): the native <c>e_sqlite3</c>/SQLitePCLRaw graph
/// stays off the dependency-light core, whose
/// <c>FileAgentRunStore</c> is the zero-dependency local default. This is
/// the opt-in robust-local tier — a single file, transactional, queryable.
/// </para>
/// </summary>
public class SqliteAgentRunStore : IAgentRunStore
{
    private readonly string _connectionString;

    public SqliteAgentRunStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(databasePath);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        EnsureSchema();
    }

    private void EnsureSchema()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "CREATE TABLE IF NOT EXISTS agent_runs (" +
            "  run_id TEXT PRIMARY KEY," +
            "  snapshot_json TEXT NOT NULL" +
            ");";
        cmd.ExecuteNonQuery();
    }

    public async ValueTask<AgentRunSnapshot?> LoadAsync(
        string runId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(runId);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT snapshot_json FROM agent_runs WHERE run_id = $id;";
        cmd.Parameters.AddWithValue("$id", runId);
        var raw = await cmd.ExecuteScalarAsync(cancellationToken) as string;
        return raw is null ? null : WebReaperAgentJson.DeserializeSnapshot(raw);
    }

    public async ValueTask SaveStepAsync(
        string runId, AgentDecision decision, AgentRunSnapshot postState,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(runId);
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(postState);
        var json = WebReaperAgentJson.SerializeSnapshot(postState);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "INSERT INTO agent_runs (run_id, snapshot_json) VALUES ($id, $json) " +
            "ON CONFLICT(run_id) DO UPDATE SET snapshot_json = excluded.snapshot_json;";
        cmd.Parameters.AddWithValue("$id", runId);
        cmd.Parameters.AddWithValue("$json", json);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public async ValueTask DeleteAsync(string runId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(runId);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM agent_runs WHERE run_id = $id;";
        cmd.Parameters.AddWithValue("$id", runId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
