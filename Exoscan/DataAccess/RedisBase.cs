using StackExchange.Redis;

namespace Exoscan.DataAccess;

public class RedisBase
{
    protected static ConnectionMultiplexer? Redis;
    
    private static readonly object _syncRoot = new();

    protected RedisBase(string connectionString)
    {
        lock (_syncRoot)
        {
            Redis = ConnectionMultiplexer.Connect(connectionString, config =>
            {
                config.AbortOnConnectFail = false;

                config.AsyncTimeout = 180000;
                config.SyncTimeout = 180000;

                config.ReconnectRetryPolicy = new ExponentialRetry(10000);
            });
        }
    }
}