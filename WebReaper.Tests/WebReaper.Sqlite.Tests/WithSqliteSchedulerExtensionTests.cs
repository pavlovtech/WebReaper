using Xunit;
using WebReaper.Builders;
using WebReaper.Sqlite;

namespace WebReaper.Sqlite.Tests;

public class WithSqliteSchedulerExtensionTests
{
    // ADR-0009/0012: WebReaper.Sqlite supplies WithSqliteScheduler as an
    // extension over the public WithScheduler registration seam, preserving
    // fluent chaining — the same satellite contract as WithRedisScheduler.
    [Fact]
    public void WithSqliteScheduler_is_a_ScraperEngineBuilder_extension_that_chains()
    {
        var builder = new ScraperEngineBuilder();

        var result = builder.WithSqliteScheduler(
            databasePath: Path.Combine(Path.GetTempPath(), $"wr-sql-{Guid.NewGuid():N}.db"),
            dataCleanupOnStart: false);

        Assert.Same(builder, result);
    }
}
