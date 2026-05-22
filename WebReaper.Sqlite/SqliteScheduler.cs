using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using WebReaper.Core.Scheduler.Abstract;
using WebReaper.Domain;
using WebReaper.Infra.Abstract;
using WebReaper.Serialization;

namespace WebReaper.Sqlite;

/// <summary>
/// ADR-0012: the local durable scheduler backed by an embedded SQLite store.
/// "Resume" is a <c>SELECT … WHERE consumed = 0 ORDER BY id</c> over an
/// indexed table — replacing <c>FileScheduler</c>'s append-only job file +
/// sidecar position file + <c>O(skip N)</c> line cursor; the cursor↔job-file
/// desync failure mode is unrepresentable (one store, one transaction). The
/// <c>Job</c> payload uses the same <see cref="WebReaperJson"/> grammar as the
/// core file scheduler and the Redis scheduler (ADR-0008), so it round-trips
/// with full type fidelity. Satellite-only (ADR-0009): the native
/// <c>e_sqlite3</c>/SQLitePCLRaw graph stays off the dependency-light core,
/// whose <c>FileScheduler</c> is unchanged and remains the zero-dependency
/// local default — this is the opt-in robust-local tier.
/// </summary>
public class SqliteScheduler : IScheduler, IAsyncInitializable
{
    private readonly string _connectionString;
    private readonly string _databasePath;
    private readonly ILogger _logger;

    public bool DataCleanupOnStart { get; set; }

    private readonly Lazy<Task> _initialization;

    public SqliteScheduler(string databasePath, ILogger logger, bool dataCleanupOnStart = false)
    {
        _databasePath = databasePath;
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        _logger = logger;
        DataCleanupOnStart = dataCleanupOnStart;

        _initialization = new Lazy<Task>(InitializeCoreAsync);
    }

    // ADR-0033: idempotent async warm-up, driven once before the crawl.
    public Task InitializeAsync() => _initialization.Value;

    private async Task InitializeCoreAsync()
    {
        // The satellite cannot (InternalsVisibleTo is core's unit tests only)
        // and should not reach core's internal FilePersistencePrep (ADR-0011);
        // it owns its own pre-write prep. SQLite creates the db file but not
        // its directory — create it eagerly and idempotently, the same
        // one-liner FilePersistencePrep.EnsureDirectory is.
        var dir = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        await using var connection = await OpenAsync();

        // WAL is a persistent db-header setting (set once; idempotent). It
        // lets the consumer read while producers write — the engine's
        // Parallel.ForEachAsync fans AddAsync out across crawl tasks while
        // GetAllAsync drains. Durability/resume is the store's transactional
        // query, NOT a hand-rolled cursor+poll+position-file (ADR-0012). A
        // write still serializes because SQLite is single-writer — that is
        // cooperating with the engine (BEGIN IMMEDIATE / busy_timeout), not a
        // hand-rolled durable queue or the rejected held-handle substrate.
        await ExecAsync(connection, "PRAGMA journal_mode=WAL;");
        await ExecAsync(connection,
            "CREATE TABLE IF NOT EXISTS jobs (" +
            " id INTEGER PRIMARY KEY AUTOINCREMENT," +
            " payload TEXT NOT NULL," +
            " consumed INTEGER NOT NULL DEFAULT 0);");
        // Partial index: the "next unconsumed" claim query is O(log n) on the
        // unconsumed rows alone, never scanning the consumed history.
        await ExecAsync(connection,
            "CREATE INDEX IF NOT EXISTS ix_jobs_unconsumed ON jobs (id) WHERE consumed = 0;");

        if (DataCleanupOnStart)
            await ExecAsync(connection, "DELETE FROM jobs;");
    }

    public async Task AddAsync(Job job, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT INTO jobs (payload) VALUES ($p);";
        cmd.Parameters.AddWithValue("$p", WebReaperJson.SerializeJob(job));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AddAsync(IEnumerable<Job> jobs, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        // deferred:false ⇒ BEGIN IMMEDIATE — take the write lock up front so a
        // batch insert never half-applies under a concurrent claim/insert
        // (Microsoft.Data.Sqlite docs: the non-deferred transaction creates
        // the write transaction immediately).
        using var tx = connection.BeginTransaction(deferred: false);
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO jobs (payload) VALUES ($p);";
        var p = cmd.Parameters.Add("$p", SqliteType.Text);
        foreach (var job in jobs)
        {
            p.Value = WebReaperJson.SerializeJob(job);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        tx.Commit();
    }

    public async IAsyncEnumerable<Job> GetAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Start {class}.{method}", nameof(SqliteScheduler), nameof(GetAllAsync));

        while (!cancellationToken.IsCancellationRequested)
        {
            var payload = await ClaimNextAsync(cancellationToken);

            if (payload is null)
            {
                // Empty-queue wait. NOT removed by the embedded store: jobs are
                // produced concurrently by in-flight crawl tasks, so the stream
                // must wait when momentarily drained — exactly as FileScheduler
                // and RedisScheduler also Task.Delay(300) poll. What the store
                // removes is the position file, the O(skip N) line cursor and
                // the cursor↔job-file desync risk (ADR-0012), not this wait.
                await Task.Delay(300, cancellationToken);
                continue;
            }

            yield return WebReaperJson.DeserializeJob(payload);
        }
    }

    // Claim-before-yield — the IScheduler family contract: FileScheduler
    // advances its position cursor before `yield` (FileScheduler.cs) and
    // RedisScheduler pops destructively (ListLeftPop) before `yield`; the role
    // interface has no ack, so "consume = claim" IS the contract, not a
    // choice. On kill -9 every not-yet-claimed row is intact and re-yielded by
    // the same WHERE consumed = 0 query; the single in-flight job is not
    // re-yielded — the same at-most-once-for-the-in-flight-job guarantee the
    // whole family already has, no weaker. deferred:false ⇒ BEGIN IMMEDIATE
    // takes the write lock up front so the SELECT-then-UPDATE claim cannot
    // race a concurrent AddAsync into SQLITE_BUSY. Committed before the
    // (arbitrarily long) yield so a slow consumer never holds the write lock.
    private async Task<string?> ClaimNextAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        using var tx = connection.BeginTransaction(deferred: false);

        long id;
        string payload;
        await using (var sel = connection.CreateCommand())
        {
            sel.Transaction = tx;
            sel.CommandText = "SELECT id, payload FROM jobs WHERE consumed = 0 ORDER BY id LIMIT 1;";
            await using var reader = await sel.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                tx.Rollback();
                return null;
            }
            id = reader.GetInt64(0);
            payload = reader.GetString(1);
        }

        await using (var upd = connection.CreateCommand())
        {
            upd.Transaction = tx;
            upd.CommandText = "UPDATE jobs SET consumed = 1 WHERE id = $id;";
            upd.Parameters.AddWithValue("$id", id);
            await upd.ExecuteNonQueryAsync(cancellationToken);
        }

        tx.Commit();
        return payload;
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        // Per-connection: cooperate with SQLite's single-writer rule instead
        // of failing fast on contention under the engine's parallel fan-out.
        await ExecAsync(connection, "PRAGMA busy_timeout=30000;");
        return connection;
    }

    private static async Task ExecAsync(SqliteConnection connection, string sql)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }
}
