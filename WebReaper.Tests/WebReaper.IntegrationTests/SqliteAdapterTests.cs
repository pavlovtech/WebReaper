using System.Collections.Concurrent;
using WebReaper.Builders;
using WebReaper.Domain.Parsing;
using WebReaper.IntegrationTests.Fixtures;
using WebReaper.Sinks.Models;
using WebReaper.Sqlite;
using WebReaper.TestServer;
using Xunit;
using Xunit.Abstractions;

namespace WebReaper.IntegrationTests;

/// <summary>
/// SQLite scheduler + visited-link tracker. SQLite is embedded (native
/// e_sqlite3, no external service), so these are deterministic and fast —
/// LocalServer-tagged and gate-worthy, unlike the Docker-backed Redis/Mongo
/// adapters. The resume test is the durability proof: a tracker that persists
/// across engine restarts must not re-crawl what a prior run already visited.
/// </summary>
[Collection("LocalSite")]
[Trait("Category", "LocalServer")]
public sealed class SqliteAdapterTests
{
    private readonly LocalTestSite _site;
    private readonly ITestOutputHelper _output;

    public SqliteAdapterTests(LocalSiteFixture fixture, ITestOutputHelper output)
    {
        _site = fixture.Site;
        _output = output;
    }

    private static Schema ItemSchema() => new() { new("title", ".title") };

    private static string TempDb() =>
        Path.Combine(Path.GetTempPath(), $"wr-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task Sqlite_scheduler_drives_a_finite_crawl_to_completion()
    {
        var dbPath = TempDb();
        var records = new ConcurrentQueue<ParsedData>();

        try
        {
            await using var engine = await ScraperEngineBuilder
                .Crawl(_site.Url("/list?page=1"))
                .Extract(ItemSchema())
                .Follow("a.item")
                .WithSqliteScheduler(dbPath, dataCleanupOnStart: true)
                .Subscribe(records.Enqueue)
                .WithLogger(new TestOutputLogger(_output))
                .StopWhenAllLinksProcessed()
                .BuildAsync();

            await engine.RunAsync();

            Assert.Equal(3, records.Count);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task Sqlite_visited_tracker_persists_so_a_second_run_re_crawls_nothing()
    {
        var dbPath = TempDb();

        try
        {
            // Run 1: fresh tracker, crawl the index + 3 items.
            var first = new ConcurrentQueue<ParsedData>();
            await using (var engine = await ScraperEngineBuilder
                .Crawl(_site.Url("/list?page=1"))
                .Extract(ItemSchema())
                .Follow("a.item")
                .TrackVisitedLinksInSqlite(dbPath, dataCleanupOnStart: true)
                .Subscribe(first.Enqueue)
                .WithLogger(new TestOutputLogger(_output))
                .StopWhenAllLinksProcessed()
                .BuildAsync())
            {
                await engine.RunAsync();
            }

            Assert.Equal(3, first.Count);

            // Run 2: SAME db, resume (no cleanup). The start URL is already a
            // member, so it is skipped, no items are discovered, nothing emits.
            var second = new ConcurrentQueue<ParsedData>();
            await using (var engine = await ScraperEngineBuilder
                .Crawl(_site.Url("/list?page=1"))
                .Extract(ItemSchema())
                .Follow("a.item")
                .TrackVisitedLinksInSqlite(dbPath, dataCleanupOnStart: false)
                .Subscribe(second.Enqueue)
                .WithLogger(new TestOutputLogger(_output))
                .StopWhenAllLinksProcessed()
                .BuildAsync())
            {
                await engine.RunAsync();
            }

            Assert.Empty(second);
        }
        finally
        {
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }
}
