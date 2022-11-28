using StackExchange.Redis;

namespace Exoscan.DataAccess;

public class RedisBase
{
    protected static ConnectionMultiplexer? Redis;
    
    private static readonly object _syncRoot = new();

    private static bool isInitialized = false;

    protected RedisBase(string connectionString)
    {
        if (isInitialized) return;

            lock (_syncRoot)
        {
            if (isInitialized) return;
            
            Redis = ConnectionMultiplexer.Connect(connectionString, config =>
            {
                config.AbortOnConnectFail = false;

                config.AsyncTimeout = 180000;
                config.SyncTimeout = 180000;

                config.ReconnectRetryPolicy = new ExponentialRetry(10000);
            });

            isInitialized = true;
        }
    }
}