using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WebReaper.Builders;

namespace WebReaper.Cosmos;

/// <summary>
/// ADR-0009: <c>WriteToCosmosDb</c> lives here, in the WebReaper.Cosmos
/// satellite, as sugar over <see cref="ScraperEngineBuilder"/>'s public
/// <c>AddSink</c> registration seam — not in core. Core no longer references
/// Microsoft.Azure.Cosmos. A satellite extension cannot reach the builder's
/// private logger, so the logger is an explicit optional argument
/// (defaulting to <see cref="NullLogger"/>) rather than the builder's.
/// </summary>
public static class CosmosSinkBuilderExtensions
{
    /// <summary>
    /// Registers an Azure Cosmos DB sink: each parsed item is written as a
    /// document to the given container, over <see cref="ScraperEngineBuilder"/>'s
    /// public <c>AddSink</c> seam.
    /// </summary>
    /// <param name="builder">The scraper engine builder to add the sink to.</param>
    /// <param name="endpointUrl">Cosmos DB account endpoint URL.</param>
    /// <param name="authorizationKey">Cosmos DB account authorization key.</param>
    /// <param name="databaseId">Target database id.</param>
    /// <param name="containerId">Target container id.</param>
    /// <param name="dataCleanupOnStart">When <see langword="true"/>, existing data is cleared when the scrape starts.</param>
    /// <param name="logger">Optional logger; defaults to <see cref="NullLogger"/> (a satellite extension cannot reach the builder's private logger).</param>
    /// <returns>The same <see cref="ScraperEngineBuilder"/>, for chaining.</returns>
    public static ScraperEngineBuilder WriteToCosmosDb(
        this ScraperEngineBuilder builder,
        string endpointUrl,
        string authorizationKey,
        string databaseId,
        string containerId,
        bool dataCleanupOnStart,
        ILogger? logger = null)
    {
        return builder.AddSink(new CosmosSink(
            endpointUrl,
            authorizationKey,
            databaseId,
            containerId,
            dataCleanupOnStart,
            logger ?? NullLogger.Instance));
    }
}
