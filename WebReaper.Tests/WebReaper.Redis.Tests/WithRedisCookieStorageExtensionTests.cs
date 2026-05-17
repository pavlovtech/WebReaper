using Xunit;
using WebReaper.Builders;
using WebReaper.Redis;

namespace WebReaper.Redis.Tests;

public class WithRedisCookieStorageExtensionTests
{
    // Same satellite contract as WriteToRedis, over the public
    // WithCookieStorage registration seam: WebReaper.Redis supplies
    // WithRedisCookieStorage as an extension that preserves fluent chaining.
    [Fact]
    public void WithRedisCookieStorage_is_a_ScraperEngineBuilder_extension_that_chains()
    {
        var builder = new ScraperEngineBuilder();

        var result = builder.WithRedisCookieStorage(
            connectionString: "localhost:6379",
            redisKey: "cookies");

        Assert.Same(builder, result);
    }
}
