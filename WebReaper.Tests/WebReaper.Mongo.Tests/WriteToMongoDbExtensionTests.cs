using Xunit;
using WebReaper.Builders;
using WebReaper.Mongo;

namespace WebReaper.Mongo.Tests;

public class WriteToMongoDbExtensionTests
{
    // The satellite's contract: WebReaper.Mongo supplies WriteToMongoDb as an
    // extension over ScraperEngineBuilder's public AddSink registration seam,
    // and it preserves the fluent-chaining behaviour every builder method (and
    // every Example) depends on. The deeper "writes to MongoDB" behaviour is
    // integration-only (live server) and is preserved by moving MongoDbSink
    // verbatim — covered by the guardrail, not re-asserted here.
    [Fact]
    public void WriteToMongoDb_is_a_ScraperEngineBuilder_extension_that_chains()
    {
        var builder = new ScraperEngineBuilder();

        var result = builder.WriteToMongoDb(
            connectionString: "mongodb://localhost:27017",
            databaseName: "db",
            collectionName: "items",
            dataCleanupOnStart: false);

        Assert.Same(builder, result);
    }
}
