using Xunit;
using WebReaper.Builders;
using WebReaper.Redis;

namespace WebReaper.Redis.Tests;

public class WriteToRedisExtensionTests
{
    // The satellite's contract: WebReaper.Redis supplies WriteToRedis as an
    // extension over ScraperEngineBuilder's public AddSink registration seam,
    // and it preserves the fluent-chaining behaviour every builder method (and
    // every Example) depends on. AbortOnConnectFail=false (ADR 0005
    // RedisConnectionPool) means constructing the sink never throws offline.
    [Fact]
    public void WriteToRedis_is_a_ScraperEngineBuilder_extension_that_chains()
    {
        var builder = new ScraperEngineBuilder();

        var result = builder.WriteToRedis(
            connectionString: "localhost:6379",
            redisKey: "items",
            dataCleanupOnStart: false);

        Assert.Same(builder, result);
    }
}
