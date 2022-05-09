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
            Connection = "Endpoint=sb://webreaper.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=mIHXjIKh6I89CHyMM2SDMr7YxvVTDFQvL+/FKlbK43g=")]string queueItem, ILogger log)
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

            var redisConnectionString = "webreaper.redis.cache.windows.net:6380,password=AIWM15Q0XAKjfZYUc9ickXfwi8O3Ti9UFAzCaAnMeEc=,ssl=True,abortConnect=False";

            var azureSBConnectionString = "Endpoint=sb://webreaper.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=mIHXjIKh6I89CHyMM2SDMr7YxvVTDFQvL+/FKlbK43g=";

            var spider = new SpiderBuilder()
                .WithLinkTracker(new RedisCrawledLinkTracker(redisConnectionString))
                .WithJobQueueReader(new AzureJobQueueReader(azureSBConnectionString, "jobqueue"))
                .WithJobQueueWriter(new AzureJobQueueWriter(azureSBConnectionString, "jobqueue"))
                .IgnoreUrls(blackList)
                .Build();

            await spider.CrawlAsync(job);

        }
    }
}
