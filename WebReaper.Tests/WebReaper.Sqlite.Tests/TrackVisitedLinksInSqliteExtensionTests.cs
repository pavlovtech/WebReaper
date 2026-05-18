using Xunit;
using WebReaper.Builders;
using WebReaper.Sqlite;

namespace WebReaper.Sqlite.Tests;

public class TrackVisitedLinksInSqliteExtensionTests
{
    // ADR-0009/0012: TrackVisitedLinksInSqlite is an extension over the
    // public WithLinkTracker registration seam, preserving fluent chaining —
    // the same satellite contract as TrackVisitedLinksInRedis.
    [Fact]
    public void TrackVisitedLinksInSqlite_is_a_ScraperEngineBuilder_extension_that_chains()
    {
        var builder = new ScraperEngineBuilder();

        var result = builder.TrackVisitedLinksInSqlite(
            databasePath: Path.Combine(Path.GetTempPath(), $"wr-sqlvt-{Guid.NewGuid():N}.db"),
            dataCleanupOnStart: false);

        Assert.Same(builder, result);
    }
}
