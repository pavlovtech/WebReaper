using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WebReaper.Core.Sinks.Concrete;
using WebReaper.Core.LinkTracker.Abstract;
using WebReaper.Core.LinkTracker.Concrete;

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