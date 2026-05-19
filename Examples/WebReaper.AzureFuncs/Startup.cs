using WebReaper.ConfigStorage.Abstract;
using WebReaper.ConfigStorage.Concrete;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WebReaper.Core.LinkTracker.Abstract;
using WebReaper.Core.LinkTracker.Concrete;
using WebReaper.Core.Crawling.Abstract;
using WebReaper.Cosmos;
using WebReaper.Redis;

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

            // ADR-0022 slice 4: the distributed Outstanding-work latch — an
            // atomic Redis counter + SET-NX one-shot fence over the shared
            // ADR-0005 pool. The distributed Crawl driver (WebReaperSpider)
            // detects "stop cleanly when work runs out" through this;
            // StartScraping seeds it.
            builder.Services.AddSingleton<IOutstandingWorkLatch>(sp => new RedisOutstandingWorkLatch("", "rutracker-crawl"));

            builder.Services.AddSingleton<IScraperConfigStorage>(sp => new InMemoryScraperConfigStorage());
        }
    }
}