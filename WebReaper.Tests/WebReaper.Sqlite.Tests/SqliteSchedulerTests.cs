using System.Collections.Immutable;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using WebReaper.Domain;
using WebReaper.Domain.PageActions;
using WebReaper.Domain.Selectors;
using WebReaper.Sqlite;
using Xunit;

namespace WebReaper.Sqlite.Tests;

// ADR-0012 acceptance criteria, pinned through the public IScheduler:
// the Job round-trips with full type fidelity (same WebReaperJson grammar as
// the core/Redis schedulers, ADR-0008); resume-after-reopen yields exactly
// the not-yet-claimed jobs in order (the position-file-killer — there is no
// position file); DataCleanupOnStart clears the table at start.
public class SqliteSchedulerTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), $"wr-sql-{Guid.NewGuid():N}");

    private string DbPath => Path.Combine(_dir, "nested", "state.db");

    private static Job UrlJob(string url) =>
        new(url, ImmutableQueue<LinkPathSelector>.Empty, ImmutableQueue<string>.Empty);

    // GetAllAsync is an infinite stream (it polls when momentarily drained,
    // exactly like FileScheduler/RedisScheduler); take exactly n, with a
    // generous timeout purely as a hang fuse.
    private static async Task<List<Job>> TakeAsync(SqliteScheduler s, int n)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var got = new List<Job>();
        await foreach (var job in s.GetAllAsync(cts.Token))
        {
            got.Add(job);
            if (got.Count == n) break;
        }
        return got;
    }

    [Fact]
    public async Task Job_round_trips_with_type_fidelity()
    {
        var job = new Job(
            "https://x.test/p",
            ImmutableQueue.CreateRange(new[]
            {
                new LinkPathSelector("a.cat", null, PageType.Static),
                new LinkPathSelector("a.item", "a.next", PageType.Dynamic)
            }),
            ImmutableQueue.CreateRange(new[] { "https://x.test", "https://x.test/c" }),
            PageType.Dynamic,
            new List<PageAction> { new(PageActionType.Click, "button#go", 42) });

        var scheduler = new SqliteScheduler(DbPath, NullLogger.Instance);
        await scheduler.Initialization;
        await scheduler.AddAsync(job);

        var got = (await TakeAsync(scheduler, 1)).Single();

        Assert.Equal("https://x.test/p", got.Url);
        Assert.Equal(PageType.Dynamic, got.PageType);
        var chain = got.LinkPathSelectors.ToArray();
        Assert.Equal(2, chain.Length);
        Assert.Equal("a.cat", chain[0].Selector);
        Assert.Equal("a.next", chain[1].PaginationSelector);
        Assert.Equal(PageType.Dynamic, chain[1].PageType);
        Assert.Equal(new[] { "https://x.test", "https://x.test/c" },
            got.ParentBacklinks.ToArray());
        Assert.Equal(PageActionType.Click, got.PageActions![0].Type);
        Assert.Equal("button#go", got.PageActions![0].Parameters[0].ToString());
        Assert.Equal(42, Convert.ToInt32(got.PageActions![0].Parameters[1]));
    }

    [Fact]
    public async Task Resume_after_reopen_yields_only_unclaimed_jobs_in_order()
    {
        // A path in a not-yet-existing directory must work (the satellite
        // creates it — there is no core FilePersistencePrep reach).
        var first = new SqliteScheduler(DbPath, NullLogger.Instance);
        await first.Initialization;
        await first.AddAsync(Enumerable.Range(1, 5).Select(i => UrlJob($"https://x/{i}")));

        var claimed = await TakeAsync(first, 2);
        Assert.Equal(new[] { "https://x/1", "https://x/2" }, claimed.Select(j => j.Url));

        // Fresh instance, same db file, no cleanup: resume is just the query.
        var resumed = new SqliteScheduler(DbPath, NullLogger.Instance);
        await resumed.Initialization;

        var rest = await TakeAsync(resumed, 3);
        Assert.Equal(new[] { "https://x/3", "https://x/4", "https://x/5" },
            rest.Select(j => j.Url));
    }

    [Fact]
    public async Task DataCleanupOnStart_clears_a_preexisting_job_table()
    {
        var first = new SqliteScheduler(DbPath, NullLogger.Instance);
        await first.Initialization;
        await first.AddAsync(Enumerable.Range(1, 3).Select(i => UrlJob($"https://stale/{i}")));

        var fresh = new SqliteScheduler(DbPath, NullLogger.Instance, dataCleanupOnStart: true);
        await fresh.Initialization;
        await fresh.AddAsync(UrlJob("https://fresh/1"));

        // Only the post-cleanup job remains; the three stale ones are gone.
        var got = await TakeAsync(fresh, 1);
        Assert.Equal(new[] { "https://fresh/1" }, got.Select(j => j.Url));
    }

    [Fact]
    public async Task Batch_AddAsync_then_claim_preserves_FIFO_order()
    {
        var scheduler = new SqliteScheduler(DbPath, NullLogger.Instance);
        await scheduler.Initialization;
        await scheduler.AddAsync(new[] { "https://a/1", "https://a/2", "https://a/3" }
            .Select(UrlJob));

        var got = await TakeAsync(scheduler, 3);
        Assert.Equal(new[] { "https://a/1", "https://a/2", "https://a/3" },
            got.Select(j => j.Url));
    }

    public void Dispose()
    {
        // Release pooled native file handles before deleting the temp tree.
        SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch { /* best-effort temp cleanup */ }
    }
}
