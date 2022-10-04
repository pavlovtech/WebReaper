using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Microsoft.Extensions.Logging;
using WebReaper.Core.LinkTracker;
using WebReaper.Core.Sinks;
using WebReaper.Abstractions.LinkTracker;

[assembly: FunctionsStartup(typeof(WebReaper.AzureFuncs.Startup))]

namespace WebReaper.AzureFuncs
{
    public class Startup : FunctionsStartup
    {
        public override void Configure(IFunctionsHostBuilder builder)
        {
            builder.Services.AddSingleton<CosmosSink>(sp => new CosmosSink("https://webreaperdbserverless.documents.azure.com:443/",
                "TssEjPIdgShphVKhFkxrAu6WJovPdIZLTFNshJWGdXuitWPIMlXTidc05WFqm20qFVz8leE8zc5JBOphlNmRYg==",
                "WebReaper",
                "Rutracker",
                sp.GetService<ILogger>()));

            builder.Services.AddSingleton<ICrawledLinkTracker>(sp => new RedisCrawledLinkTracker("webreaper.redis.cache.windows.net:6380,password=etUgOS0XUTTpZqNGlSlmaczrDKTeySPBWAzCaAMhsVU=,ssl=True,abortConnect=False"));
        }
    }
}