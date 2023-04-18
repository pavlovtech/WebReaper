using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using WebReaper.Builders;
using WebReaper.Core.LinkTracker.Abstract;
using WebReaper.Sinks.Concrete;
using WebReaper.Domain;

namespace WebReaper.AzureFuncs
{
    public class WebReaperSpider
    {
        public IVisitedLinkTracker LinkTracker { get; }
        public CosmosSink CosmosSink { get; }

        public WebReaperSpider(IVisitedLinkTracker linkTracker, CosmosSink cosmosSink)
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

        [FunctionName("ExoScraperSpider")]
        public async Task Run([ServiceBusTrigger("jobqueue", Connection = "ServiceBusConnectionString")]string myQueueItem,
            [ServiceBus("jobqueue", Connection = "ServiceBusConnectionString")]IAsyncCollector<string> outputSbQueue, ILogger log)
        {
            log.LogInformation($"C# ServiceBus queue trigger function processed message: {myQueueItem}");
            
            var job = JsonConvert.DeserializeObject<Job>(myQueueItem, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore
            });

            var spider = new SpiderBuilder()
                .WithLogger(log)
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
