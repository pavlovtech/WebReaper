using WebReaper.Builders;

namespace WebReaper.AzureServiceBus;

/// <summary>
/// ADR-0009: the Azure Service Bus builder sugar lives here, in the
/// WebReaper.AzureServiceBus satellite, over <see cref="ScraperEngineBuilder"/>'s
/// public <c>WithScheduler</c> registration seam — not in core. Core no longer
/// references Azure.Messaging.ServiceBus. The scheduler takes no logger, so
/// (unlike the Redis sugar) this extension has no logger argument.
/// </summary>
public static class AzureServiceBusBuilderExtensions
{
    /// <summary>
    /// Uses an Azure Service Bus queue as the scheduler, over
    /// <see cref="ScraperEngineBuilder"/>'s public <c>WithScheduler</c> seam —
    /// so multiple workers and serverless functions can share crawl state
    /// (distributed mode).
    /// </summary>
    /// <param name="builder">The scraper engine builder.</param>
    /// <param name="connectionString">Azure Service Bus connection string.</param>
    /// <param name="queueName">Service Bus queue used as the shared job queue.</param>
    /// <param name="dataCleanupOnStart">When <see langword="true"/>, the queue is drained when the scrape starts.</param>
    /// <returns>The same <see cref="ScraperEngineBuilder"/>, for chaining.</returns>
    public static ScraperEngineBuilder WithAzureServiceBusScheduler(
        this ScraperEngineBuilder builder,
        string connectionString,
        string queueName,
        bool dataCleanupOnStart = false)
    {
        return builder.WithScheduler(new AzureServiceBusScheduler(
            connectionString,
            queueName,
            dataCleanupOnStart));
    }
}
