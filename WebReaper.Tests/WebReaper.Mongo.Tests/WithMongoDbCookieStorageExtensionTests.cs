using Xunit;
using WebReaper.Builders;
using WebReaper.Mongo;

namespace WebReaper.Mongo.Tests;

public class WithMongoDbCookieStorageExtensionTests
{
    // Same satellite contract as WriteToMongoDb, over the public
    // WithCookieStorage registration seam: WebReaper.Mongo supplies
    // WithMongoDbCookieStorage as an extension that preserves fluent chaining.
    [Fact]
    public void WithMongoDbCookieStorage_is_a_ScraperEngineBuilder_extension_that_chains()
    {
        var builder = new ScraperEngineBuilder();

        var result = builder.WithMongoDbCookieStorage(
            connectionString: "mongodb://localhost:27017",
            databaseName: "db",
            collectionName: "cookies",
            cookieCollectionId: "cookies-1");

        Assert.Same(builder, result);
    }
}
