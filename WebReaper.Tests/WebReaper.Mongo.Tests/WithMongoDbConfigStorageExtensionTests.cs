using Xunit;
using WebReaper.Builders;
using WebReaper.Mongo;

namespace WebReaper.Mongo.Tests;

public class WithMongoDbConfigStorageExtensionTests
{
    // Same satellite contract as WriteToMongoDb, over the public
    // WithConfigStorage registration seam: WebReaper.Mongo supplies
    // WithMongoDbConfigStorage as an extension that preserves fluent chaining.
    [Fact]
    public void WithMongoDbConfigStorage_is_a_ScraperEngineBuilder_extension_that_chains()
    {
        var builder = new ScraperEngineBuilder();

        var result = builder.WithMongoDbConfigStorage(
            connectionString: "mongodb://localhost:27017",
            databaseName: "db",
            collectionName: "config",
            configId: "config-1");

        Assert.Same(builder, result);
    }
}
