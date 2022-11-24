using Exoscan.ConfigStorage.Abstract;
using Exoscan.ConfigStorage.Concrete;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Exoscan.LinkTracker.Concrete;
using Exoscan.LinkTracker.Abstract;
using Exoscan.Sinks.Concrete;

[assembly: FunctionsStartup(typeof(Exoscan.AzureFuncs.Startup))]

namespace Exoscan.AzureFuncs
{
    public class Startup : FunctionsStartup
    {
        public override void Configure(IFunctionsHostBuilder builder)
        {
            builder.Services.AddSingleton<CosmosSink>(sp => new CosmosSink("",
                "",
                "Exoscan",
                "container",
                sp.GetService<ILogger>()));

            builder.Services.AddSingleton<IVisitedLinkTracker>(sp => new RedisVisitedLinkTracker("", "rutracker-visited-links"));
            builder.Services.AddSingleton<IScraperConfigStorage>(sp => new InMemoryScraperConfigStorage());
        }
    }
}