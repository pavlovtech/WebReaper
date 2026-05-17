using Xunit;
using WebReaper.Builders;
using WebReaper.AzureServiceBus;

namespace WebReaper.AzureServiceBus.Tests;

public class WithAzureServiceBusSchedulerExtensionTests
{
    // ADR-0009 satellite contract over the public WithScheduler registration
    // seam: WebReaper.AzureServiceBus supplies WithAzureServiceBusScheduler as
    // a ScraperEngineBuilder extension that preserves fluent chaining — not a
    // core builder method (core no longer references Azure.Messaging.ServiceBus).
    // Offline-safe: a well-formed connection string parses without dialing the
    // broker (the AMQP link is lazy), and dataCleanupOnStart:false makes the
    // scheduler's InitializeAsync a no-op, so no Service Bus namespace is
    // needed to assert the builder wiring.
    [Fact]
    public void WithAzureServiceBusScheduler_is_a_ScraperEngineBuilder_extension_that_chains()
    {
        var builder = new ScraperEngineBuilder();

        var result = builder.WithAzureServiceBusScheduler(
            connectionString: "Endpoint=sb://localhost.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=fake",
            queueName: "jobs",
            dataCleanupOnStart: false);

        Assert.Same(builder, result);
    }
}
