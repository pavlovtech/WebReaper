using Xunit;
using WebReaper.Builders;
using WebReaper.Redis;

namespace WebReaper.Redis.Tests;

public class TrackVisitedLinksInRedisExtensionTests
{
    // Same satellite contract as WriteToRedis, over the public WithLinkTracker
    // registration seam: WebReaper.Redis supplies TrackVisitedLinksInRedis as
    // an extension that preserves fluent chaining.
    [Fact]
    public void TrackVisitedLinksInRedis_is_a_ScraperEngineBuilder_extension_that_chains()
    {
        var builder = new ScraperEngineBuilder();

        var result = builder.TrackVisitedLinksInRedis(
            connectionString: "localhost:6379",
            redisKey: "visited-links",
            dataCleanupOnStart: false);

        Assert.Same(builder, result);
    }
}
