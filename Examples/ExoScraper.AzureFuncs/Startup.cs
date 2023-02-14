using ExoScraper.ConfigStorage.Abstract;
using ExoScraper.ConfigStorage.Concrete;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ExoScraper.LinkTracker.Concrete;
using ExoScraper.LinkTracker.Abstract;
using ExoScraper.Sinks.Concrete;

[assembly: FunctionsStartup(typeof(ExoScraper.AzureFuncs.Startup))]

namespace ExoScraper.AzureFuncs
{
    public class Startup : FunctionsStartup
    {
        public override void Configure(IFunctionsHostBuilder builder)
        {
            builder.Services.AddSingleton<CosmosSink>(sp => new CosmosSink("",
                "",
                "ExoScraper",
                "container",
                sp.GetService<ILogger>()));

            builder.Services.AddSingleton<IVisitedLinkTracker>(sp => new RedisVisitedLinkTracker("", "rutracker-visited-links"));
            builder.Services.AddSingleton<IScraperConfigStorage>(sp => new InMemoryScraperConfigStorage());
        }
    }
}