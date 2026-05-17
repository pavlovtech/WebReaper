using Xunit;
using WebReaper.Redis;

namespace WebReaper.Redis.Tests;

// Pins the exact bug the removed RedisBase had (ADR 0005): its process-static,
// first-connection-wins multiplexer silently coalesced every connection string
// after the first onto one connection. The pool's contract is the opposite —
// same string ⇒ same multiplexer, different string ⇒ different multiplexer.
// Offline-safe by the verified StackExchange.Redis contract: the pool sets
// AbortOnConnectFail = false, under which Connect returns a background-
// reconnecting multiplexer instead of throwing when no server is reachable, so
// these dead loopback endpoints never need a Redis server.
public class RedisConnectionPoolTests
{
    [Fact]
    public void Same_connection_string_returns_the_same_multiplexer()
    {
        var a = RedisConnectionPool.Get("localhost:6390");
        var b = RedisConnectionPool.Get("localhost:6390");

        Assert.Same(a, b);
    }

    [Fact]
    public void Different_connection_strings_return_different_multiplexers()
    {
        // The RedisBase regression: this is exactly what first-wins got wrong.
        var a = RedisConnectionPool.Get("localhost:6391");
        var b = RedisConnectionPool.Get("localhost:6392");

        Assert.NotSame(a, b);
    }

    [Fact]
    public void GetDatabase_resolves_a_database_without_a_server()
    {
        Assert.NotNull(RedisConnectionPool.GetDatabase("localhost:6393"));
    }
}
