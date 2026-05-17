using Xunit;
using WebReaper.Builders;
using WebReaper.Redis;

namespace WebReaper.Redis.Tests;

public class WithRedisConfigStorageExtensionTests
{
    // Same satellite contract as WriteToRedis, over the public
    // WithConfigStorage registration seam: WebReaper.Redis supplies
    // WithRedisConfigStorage as an extension that preserves fluent chaining.
    [Fact]
    public void WithRedisConfigStorage_is_a_ScraperEngineBuilder_extension_that_chains()
    {
        var builder = new ScraperEngineBuilder();

        var result = builder.WithRedisConfigStorage(
            connectionString: "localhost:6379",
            redisKey: "scraper-config");

        Assert.Same(builder, result);
    }
}
