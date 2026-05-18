using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using WebReaper.Builders;
using WebReaper.Core.Crawling;
using WebReaper.Core.LinkTracker.Abstract;
using WebReaper.Cosmos;
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

            // ADR-0009: SpiderBuilder is internal; the bare-spider seam for
            // the distributed-worker pattern is ScraperEngineBuilder.BuildSpider().
            var spider = new ScraperEngineBuilder()
                .WithLogger(log)
                .WithLinkTracker(LinkTracker)
                .AddSink(CosmosSink)
                .BuildSpider();

            // ADR-0022 slice 1: ISpider.CrawlAsync now returns a JobReport
            // (the Crawl step's outcome + the loaded doc). This worker IS the
            // distributed Crawl driver; its full reshape — Sink fan-out to
            // CosmosSink, visited-link tracking, and the Outstanding-work
            // latch over the shared tracker — is ADR-0022 slice 4. Here we
            // only re-enqueue discovered child Jobs, so the example compiles
            // and crawl progression is preserved (sink/tracking land in s4).
            var report = await spider.CrawlAsync(job);

            foreach (var newJob in report.Outcome.NextJobs)
            {
                log.LogInformation($"Adding to the queue: {newJob.Url}");
                await outputSbQueue.AddAsync(SerializeToJson(newJob));
            }
        }
    }
}
