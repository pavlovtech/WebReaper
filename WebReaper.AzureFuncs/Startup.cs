using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

[assembly: FunctionsStartup(typeof(WebReaper.AzureFuncs.Startup))]

namespace WebReaper.AzureFuncs
{
    public class Startup : FunctionsStartup
    {
        public override void Configure(IFunctionsHostBuilder builder)
        {
            builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
            {
                var r = ConnectionMultiplexer.Connect("webreaper.redis.cache.windows.net:6380,password=etUgOS0XUTTpZqNGlSlmaczrDKTeySPBWAzCaAMhsVU=,ssl=True,abortConnect=False",
                    config => {
                        config.SyncTimeout = 10000;
                        config.AsyncTimeout = 10000;
                        config.ConnectTimeout = 20000;
                        config.AbortOnConnectFail = false;
                        config.ConnectRetry = 5;
                    });
                return r;
            });

            builder.Services.AddSingleton(new CosmosClient("https://webreaper.documents.azure.com:443/",
                "XkMSndeYQ1285XrVRNG7MYVg3YUw32aOPPpYyS8YDIcKa8SxMK5cqwsg069jlFW2oOdxedg92qQieZd0IO4Qtw=="));
        }
    }
}