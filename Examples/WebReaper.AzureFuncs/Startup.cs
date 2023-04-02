using WebReaper.ConfigStorage.Abstract;
using WebReaper.ConfigStorage.Concrete;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WebReaper.Core.LinkTracker.Abstract;
using WebReaper.Core.LinkTracker.Concrete;
using WebReaper.Sinks.Concrete;

[assembly: FunctionsStartup(typeof(WebReaper.AzureFuncs.Startup))]

namespace WebReaper.AzureFuncs
{
    public class Startup : FunctionsStartup
    {
        public override void Configure(IFunctionsHostBuilder builder)
        {
            builder.Services.AddSingleton<CosmosSink>(sp => new CosmosSink("",
                "",
                "WebReaper",
                "container",
                false,
                sp.GetService<ILogger>()));

            builder.Services.AddSingleton<IVisitedLinkTracker>(sp => new RedisVisitedLinkTracker("", "rutracker-visited-links"));
            builder.Services.AddSingleton<IScraperConfigStorage>(sp => new InMemoryScraperConfigStorage());
        }
    }
}