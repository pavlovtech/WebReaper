using WebReaper.Sinks.Models;

namespace WebReaper.Sinks.Abstract;

/// <summary>
/// A destination for scraped data. The Crawl driver (ADR-0022) fans every
/// target-page <see cref="ParsedData"/> out to every registered sink, so
/// sinks compose (console + file + database at once). Built-in adapters are
/// added via builder methods (<c>.WriteToConsole()</c>,
/// <c>.WriteToJsonFile()</c>, <c>.WriteToCsvFile()</c>, the satellite db
/// sinks); implement this to add your own.
/// </summary>
public interface IScraperSink
{
    /// <summary>
    /// Wipe the destination when the crawl starts instead of appending to it.
    /// Note the documented asymmetry: <c>WriteToJsonFile</c> defaults this to
    /// <c>true</c> (fresh file each run); the other file sinks default it to
    /// <c>false</c> (append).
    /// </summary>
    public bool DataCleanupOnStart { get; set; }

    /// <summary>
    /// Write one parsed record. Called once per target page per sink, and
    /// concurrently — the driver fans out under <c>Parallel.ForEachAsync</c>,
    /// so an implementation must be safe under concurrent calls.
    ///
    /// ADR-0031: each sink receives its own <see cref="ParsedData"/> — the
    /// driver deep-clones <see cref="ParsedData.Data"/> per sink — so a sink
    /// may mutate <c>entity.Data</c> freely. The page URL is already folded
    /// into <c>Data</c> under <c>"url"</c>; a sink writes <c>Data</c> as-is and
    /// need not merge the URL itself.
    /// </summary>
    public Task EmitAsync(ParsedData entity, CancellationToken cancellationToken = default);
}
