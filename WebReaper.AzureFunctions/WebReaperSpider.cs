using System;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using WebReaper.Domain;
using WebReaper.LinkTracker;
using WebReaper.Queue.AzureServiceBus;
using WebReaper.Scraper;

namespace WebReaper.AzureFunctions
{
    public class WebReaperSpider
    {
        private readonly ILogger _logger;

        public WebReaperSpider(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<WebReaperSpider>();
        }

        [Function("WebReaperSpider")]
        [ServiceBusOutput("jobqueue", Connection = "ServiceBusConnectionString")]
        public void Run([ServiceBusTrigger("jobqueue", Connection = "ServiceBusConnectionString")] string myQueueItem)
        {
            _logger.LogInformation($"C# ServiceBus queue trigger function processed message: {myQueueItem}");

            var job = JsonConvert.DeserializeObject<Job>(myQueueItem, new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto
            });

            var blackList = new string[] {
                "https://rutracker.org/forum/viewforum.php?f=396",
                "https://rutracker.org/forum/viewforum.php?f=2322",
                "https://rutracker.org/forum/viewforum.php?f=1993",
                "https://rutracker.org/forum/viewforum.php?f=2167",
                "https://rutracker.org/forum/viewforum.php?f=2321"
            };

            var redisConnectionString = "webreaper.redis.cache.windows.net:6380,password=etUgOS0XUTTpZqNGlSlmaczrDKTeySPBWAzCaAMhsVU=,ssl=True,abortConnect=False";

            var spiderBuilder = new SpiderBuilder()
                .WithLogger(_logger)
                .IgnoreUrls(blackList)
                .WithLinkTracker(new RedisCrawledLinkTracker(redisConnectionString))
                .WriteToCosmosDb(
                    "https://webreaper.documents.azure.com:443/",
                    "XkMSndeYQ1285XrVRNG7MYVg3YUw32aOPPpYyS8YDIcKa8SxMK5cqwsg069jlFW2oOdxedg92qQieZd0IO4Qtw==",
                    "WebReaper",
                    "Rutracker")
                .Build();

            var newJobs = spiderBuilder.CrawlAsync(job);
        }
    }
}
