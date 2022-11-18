using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Exoscan.Sinks.Concrete;
using Exoscan.Domain;
using Exoscan.LinkTracker.Abstract;
using Exoscan.Core.Builders;

namespace Exoscan.AzureFuncs
{
    public class ExoscanSpider
    {
        public IVisitedLinkTracker LinkTracker { get; }
        public CosmosSink CosmosSink { get; }

        public ExoscanSpider(IVisitedLinkTracker linkTracker, CosmosSink cosmosSink)
        {
            LinkTracker = linkTracker;
            CosmosSink = cosmosSink;
        }

        private string SerializeToJson(Job job)
        {
            var json = JsonConvert.SerializeObject(job, Formatting.Indented, new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto
            });

            return json;
        }

        [FunctionName("ExoscanSpider")]
        public async Task Run([ServiceBusTrigger("jobqueue", Connection = "ServiceBusConnectionString")]string myQueueItem,
            [ServiceBus("jobqueue", Connection = "ServiceBusConnectionString")]IAsyncCollector<string> outputSbQueue, ILogger log)
        {
            log.LogInformation($"C# ServiceBus queue trigger function processed message: {myQueueItem}");
            
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

            var spider = new SpiderBuilder()
                .WithLogger(log)
                .IgnoreUrls(blackList)
                .WithLinkTracker(LinkTracker)
                .AddSink(CosmosSink)
                .Build();

            var newJobs = await spider.CrawlAsync(job);

            foreach(var newJob in newJobs)
            {
                log.LogInformation($"Adding to the queue: {newJob.Url}");
                await outputSbQueue.AddAsync(SerializeToJson(newJob));
            }
        }
    }
}
