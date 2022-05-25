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
                return ConnectionMultiplexer.Connect("webreaper.redis.cache.windows.net:6380,password=etUgOS0XUTTpZqNGlSlmaczrDKTeySPBWAzCaAMhsVU=,ssl=True,abortConnect=False",
                    config => {
                        config.AbortOnConnectFail = false;
                    });
            });

            builder.Services.AddSingleton(new CosmosClient("https://webreaperdbserverless.documents.azure.com:443/",
                "TssEjPIdgShphVKhFkxrAu6WJovPdIZLTFNshJWGdXuitWPIMlXTidc05WFqm20qFVz8leE8zc5JBOphlNmRYg=="));
        }
    }
}