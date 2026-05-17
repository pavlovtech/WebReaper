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
