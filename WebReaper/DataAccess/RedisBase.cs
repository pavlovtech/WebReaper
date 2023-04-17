using Newtonsoft.Json;
using StackExchange.Redis;

namespace WebReaper.DataAccess;

public class RedisBase
{
    protected static ConnectionMultiplexer? Redis;

    private static readonly object _syncRoot = new();

    private static bool isInitialized;

    protected RedisBase(string connectionString)
    {
        if (isInitialized) return;

        lock (_syncRoot)
        {
            if (isInitialized) return;

            Redis = ConnectionMultiplexer.Connect(connectionString, config =>
            {
                config.AbortOnConnectFail = false;
                config.AllowAdmin = true;
                config.AsyncTimeout = 180000;
                config.SyncTimeout = 180000;

                config.ReconnectRetryPolicy = new ExponentialRetry(10000);
            });

            isInitialized = true;
        }
    }

    protected static string SerializeToJson(object config)
    {
        var json = JsonConvert.SerializeObject(config, Formatting.Indented, new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.None,
            NullValueHandling = NullValueHandling.Ignore,
            TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple
        });

        return json;
    }
}