using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using WebReaper.Domain.Parsing;
using WebReaper.Parsing;
using WebReaper.Scraper;
using WebReaper.LinkTracker;
using WebReaper.Queue.AzureServiceBus;
using WebReaper.Domain;
using Newtonsoft.Json;

namespace WebReaper.Spider
{
    public static class WebReaperSpiderFunc
    {
        [FunctionName("WebReaperSpiderFunc")]
        public static async Task Run([ServiceBusTrigger("jobqueue",
            Connection = "")]string queueItem, ILogger log)
        {
            log.LogInformation($"C# ServiceBus queue trigger function processed message: {queueItem}");

            var job = JsonConvert.DeserializeObject<Job>(queueItem, new JsonSerializerSettings
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

            var azureSBConnectionString = "Endpoint=sb://webreaper.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=g0AAACe/NXS+/qWVad4KUnnw6iGECmUTJTpfFOMfjms=";

            var spider = new SpiderBuilder()
                .WithLinkTracker(new RedisCrawledLinkTracker(redisConnectionString))
                .WithJobQueueReader(new AzureJobQueueReader(azureSBConnectionString, "jobqueue"))
                .WithJobQueueWriter(new AzureJobQueueWriter(azureSBConnectionString, "jobqueue"))
                .IgnoreUrls(blackList)
                .WriteToCosmosDb(
                    "https://webreaper.documents.azure.com:443/",
                    "XkMSndeYQ1285XrVRNG7MYVg3YUw32aOPPpYyS8YDIcKa8SxMK5cqwsg069jlFW2oOdxedg92qQieZd0IO4Qtw==",
                    "WebReaper",
                    "Rutraker")
                .Build();

            await spider.CrawlAsync(job);
        }
    }
}
