using Microsoft.Data.Sqlite;
using WebReaper.Sqlite;
using Xunit;

namespace WebReaper.Sqlite.Tests;

// ADR-0012 acceptance criteria, pinned through the public IVisitedLinkTracker:
// add is an idempotent set add (INSERT OR IGNORE); GetNotVisitedLinks returns
// only the unvisited inputs, in order; count and membership are served FROM
// the table and survive a reopen with a fresh instance (the deliberate
// no-in-memory-mirror behaviour — a brand-new instance, whose memory is
// empty, answers correctly because the table IS the set); DataCleanupOnStart
// clears the table at start.
public class SqliteVisitedLinkTrackerTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), $"wr-sqlvt-{Guid.NewGuid():N}");

    private string DbPath => Path.Combine(_dir, "nested", "visited.db");

    [Fact]
    public async Task AddVisitedLink_is_an_idempotent_set_add()
    {
        var tracker = new SqliteVisitedLinkTracker(DbPath);
        await tracker.Initialization;

        await tracker.AddVisitedLinkAsync("https://x/a");
        await tracker.AddVisitedLinkAsync("https://x/b");
        await tracker.AddVisitedLinkAsync("https://x/a"); // duplicate ignored

        Assert.Equal(2, await tracker.GetVisitedLinksCount());
        var all = await tracker.GetVisitedLinksAsync();
        Assert.Equal(new[] { "https://x/a", "https://x/b" }, all.OrderBy(x => x));
    }

    [Fact]
    public async Task GetNotVisitedLinks_returns_only_unvisited_in_input_order()
    {
        var tracker = new SqliteVisitedLinkTracker(DbPath);
        await tracker.Initialization;
        await tracker.AddVisitedLinkAsync("https://x/a");
        await tracker.AddVisitedLinkAsync("https://x/c");

        var notVisited = await tracker.GetNotVisitedLinks(
            new[] { "https://x/a", "https://x/b", "https://x/c", "https://x/d" });

        Assert.Equal(new[] { "https://x/b", "https://x/d" }, notVisited);
    }

    [Fact]
    public async Task Membership_and_count_survive_reopen_without_an_in_memory_mirror()
    {
        var first = new SqliteVisitedLinkTracker(DbPath);
        await first.Initialization;
        await first.AddVisitedLinkAsync("https://x/a");
        await first.AddVisitedLinkAsync("https://x/b");
        await first.AddVisitedLinkAsync("https://x/c");

        // Fresh instance, same db, no cleanup: its in-memory state is empty,
        // yet it answers from the table — proving the no-mirror design.
        var reopened = new SqliteVisitedLinkTracker(DbPath);
        await reopened.Initialization;

        Assert.Equal(3, await reopened.GetVisitedLinksCount());
        Assert.Equal(
            new[] { "https://x/z" },
            await reopened.GetNotVisitedLinks(new[] { "https://x/b", "https://x/z" }));
        Assert.Equal(
            new[] { "https://x/a", "https://x/b", "https://x/c" },
            (await reopened.GetVisitedLinksAsync()).OrderBy(x => x));
    }

    [Fact]
    public async Task DataCleanupOnStart_clears_a_preexisting_visited_table()
    {
        var first = new SqliteVisitedLinkTracker(DbPath);
        await first.Initialization;
        await first.AddVisitedLinkAsync("https://stale/1");
        await first.AddVisitedLinkAsync("https://stale/2");

        var fresh = new SqliteVisitedLinkTracker(DbPath, dataCleanupOnStart: true);
        await fresh.Initialization;

        Assert.Equal(0, await fresh.GetVisitedLinksCount());

        await fresh.AddVisitedLinkAsync("https://fresh/1");
        Assert.Equal(1, await fresh.GetVisitedLinksCount());
        Assert.Equal(new[] { "https://fresh/1" }, await fresh.GetVisitedLinksAsync());
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch { /* best-effort temp cleanup */ }
    }
}
