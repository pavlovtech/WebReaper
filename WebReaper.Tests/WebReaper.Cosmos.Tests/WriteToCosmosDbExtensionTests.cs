using Xunit;
using WebReaper.Builders;
using WebReaper.Cosmos;

namespace WebReaper.Cosmos.Tests;

public class WriteToCosmosDbExtensionTests
{
    // The satellite's contract: WebReaper.Cosmos supplies WriteToCosmosDb as
    // an extension over ScraperEngineBuilder's public registration seam, and
    // it preserves the fluent-chaining behaviour every builder method (and
    // every Example) depends on. The deeper "writes to Cosmos" behaviour is
    // integration-only (live account) and is preserved by moving CosmosSink
    // verbatim — covered by the guardrail, not re-asserted here.
    [Fact]
    public void WriteToCosmosDb_is_a_ScraperEngineBuilder_extension_that_chains()
    {
        var builder = new ScraperEngineBuilder();

        var result = builder.WriteToCosmosDb(
            endpointUrl: "https://account.documents.azure.com:443/",
            authorizationKey: "auth-key",
            databaseId: "db",
            containerId: "items",
            dataCleanupOnStart: false);

        Assert.Same(builder, result);
    }
}
