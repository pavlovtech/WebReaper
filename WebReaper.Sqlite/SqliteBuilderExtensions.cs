using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WebReaper.Builders;

namespace WebReaper.Sqlite;

/// <summary>
/// ADR-0009 / ADR-0012: the Sqlite builder sugar lives here, in the
/// WebReaper.Sqlite satellite, over <see cref="ScraperEngineBuilder"/>'s
/// public registration seam (<c>WithScheduler</c>) — not in core. Core does
/// not reference Microsoft.Data.Sqlite. A satellite extension cannot reach
/// the builder's private logger, so the logger is an explicit optional
/// argument (defaulting to <see cref="NullLogger"/>) rather than the
/// builder's — the same shape as <c>WithRedisScheduler</c>.
/// </summary>
public static class SqliteBuilderExtensions
{
    /// <summary>
    /// Uses a SQLite-backed embedded store as the scheduler, over
    /// <see cref="ScraperEngineBuilder"/>'s public <c>WithScheduler</c> seam.
    /// The opt-in robust-local durability tier: "resume" is a query, not a
    /// hand-rolled position file (ADR-0012). The core file scheduler stays the
    /// zero-dependency default; this is opt-in.
    /// </summary>
    /// <param name="builder">The scraper engine builder.</param>
    /// <param name="databasePath">Path to the SQLite database file (its directory is created if missing).</param>
    /// <param name="dataCleanupOnStart">When <see langword="true"/>, the job table is cleared when the scrape starts.</param>
    /// <param name="logger">Optional logger; defaults to <see cref="NullLogger"/> (a satellite extension cannot reach the builder's private logger).</param>
    /// <returns>The same <see cref="ScraperEngineBuilder"/>, for chaining.</returns>
    public static ScraperEngineBuilder WithSqliteScheduler(
        this ScraperEngineBuilder builder,
        string databasePath,
        bool dataCleanupOnStart = false,
        ILogger? logger = null)
    {
        return builder.WithScheduler(new SqliteScheduler(
            databasePath,
            logger ?? NullLogger.Instance,
            dataCleanupOnStart));
    }
}
