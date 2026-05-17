using Xunit;
using WebReaper.Builders;
using WebReaper.Redis;

namespace WebReaper.Redis.Tests;

public class WithRedisSchedulerExtensionTests
{
    // Same satellite contract as WriteToRedis, over the public WithScheduler
    // registration seam: WebReaper.Redis supplies WithRedisScheduler as an
    // extension that preserves fluent chaining.
    [Fact]
    public void WithRedisScheduler_is_a_ScraperEngineBuilder_extension_that_chains()
    {
        var builder = new ScraperEngineBuilder();

        var result = builder.WithRedisScheduler(
            connectionString: "localhost:6379",
            queueName: "jobs",
            dataCleanupOnStart: false);

        Assert.Same(builder, result);
    }
}
