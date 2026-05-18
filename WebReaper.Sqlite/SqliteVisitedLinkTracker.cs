using Microsoft.Data.Sqlite;
using WebReaper.Core.LinkTracker.Abstract;

namespace WebReaper.Sqlite;

/// <summary>
/// ADR-0012: the local durable visited-link set backed by an embedded SQLite
/// store. The <c>visited(url PRIMARY KEY)</c> table <em>is</em> the set —
/// queried directly, with <strong>no in-memory mirror</strong>. This is a
/// deliberate deviation from <c>FileVisitedLinkedTracker</c>, mirroring
/// <c>RedisVisitedLinkTracker</c> instead: the mirror is the file adapter's
/// own essence and is exactly the "load the entire visited set into process
/// memory at start" an embedded durable store exists to remove — it does not
/// survive the very-large-crawl scale at which a durable store is chosen.
/// Shape consistency across the durable tier (Redis and SQLite both "the
/// store is the source of truth") is the right consistency. The
/// <c>url PRIMARY KEY</c> index keeps the per-page <see cref="GetNotVisitedLinks"/>
/// fast and the crawl is page-fetch-I/O-bound regardless. Satellite-only
/// (ADR-0009): the native <c>e_sqlite3</c>/SQLitePCLRaw graph stays off the
/// dependency-light core, whose <c>FileVisitedLinkedTracker</c> is unchanged.
/// </summary>
public class SqliteVisitedLinkTracker : IVisitedLinkTracker
{
    private readonly string _connectionString;
    private readonly string _databasePath;

    public bool DataCleanupOnStart { get; set; }

    public Task Initialization { get; }

    public SqliteVisitedLinkTracker(string databasePath, bool dataCleanupOnStart = false)
    {
        _databasePath = databasePath;
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        DataCleanupOnStart = dataCleanupOnStart;

        Initialization = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        // SQLite creates the db file but not its directory; create it eagerly
        // and idempotently (the satellite owns its own pre-write prep — it
        // cannot reach core's internal FilePersistencePrep, ADR-0011).
        var dir = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        await using var connection = await OpenAsync();

        // WAL is a persistent db-header setting (idempotent if a sibling
        // adapter on the same file already set it). The table IS the set;
        // there is no in-memory mirror to load (ADR-0012).
        await ExecAsync(connection, "PRAGMA journal_mode=WAL;");
        await ExecAsync(connection,
            "CREATE TABLE IF NOT EXISTS visited (url TEXT PRIMARY KEY);");

        if (DataCleanupOnStart)
            await ExecAsync(connection, "DELETE FROM visited;");
    }

    public async Task AddVisitedLinkAsync(string visitedLink)
    {
        await Initialization;

        await using var connection = await OpenAsync();
        await using var cmd = connection.CreateCommand();
        // INSERT OR IGNORE = the set's idempotent add: a duplicate url hits
        // the PRIMARY KEY and is silently dropped (RedisVisitedLinkTracker's
        // SetAdd semantics).
        cmd.CommandText = "INSERT OR IGNORE INTO visited (url) VALUES ($u);";
        cmd.Parameters.AddWithValue("$u", visitedLink);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<string>> GetVisitedLinksAsync()
    {
        await Initialization;

        await using var connection = await OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT url FROM visited;";
        var result = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(reader.GetString(0));
        return result;
    }

    public async Task<List<string>> GetNotVisitedLinks(IEnumerable<string> links)
    {
        await Initialization;

        // Membership-per-link against the indexed PRIMARY KEY, input order
        // preserved — RedisVisitedLinkTracker's links.Where(!SetContains)
        // semantics. Deliberately NOT a full SELECT url FROM visited: that
        // would be the in-memory-mirror load ADR-0012 removed.
        var candidates = links as ICollection<string> ?? links.ToList();

        await using var connection = await OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT EXISTS(SELECT 1 FROM visited WHERE url = $u);";
        var p = cmd.Parameters.Add("$u", SqliteType.Text);

        var notVisited = new List<string>();
        foreach (var link in candidates)
        {
            p.Value = link;
            var exists = Convert.ToInt64(await cmd.ExecuteScalarAsync()) == 1;
            if (!exists)
                notVisited.Add(link);
        }
        return notVisited;
    }

    public async Task<long> GetVisitedLinksCount()
    {
        await Initialization;

        await using var connection = await OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM visited;";
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private async Task<SqliteConnection> OpenAsync()
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        // Cooperate with SQLite's single-writer rule under the engine's
        // parallel fan-out instead of failing fast on contention.
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
