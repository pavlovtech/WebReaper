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
