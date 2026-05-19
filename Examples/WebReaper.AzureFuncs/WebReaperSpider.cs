using System.Collections.Immutable;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using WebReaper.Builders;
using WebReaper.Core.Crawling;
using WebReaper.Core.Crawling.Abstract;
using WebReaper.Core.LinkTracker.Abstract;
using WebReaper.Cosmos;
using WebReaper.Domain;

namespace WebReaper.AzureFuncs
{
    public class WebReaperSpider
    {
        public IVisitedLinkTracker LinkTracker { get; }
        public CosmosSink CosmosSink { get; }
        public IOutstandingWorkLatch Latch { get; }

        public WebReaperSpider(IVisitedLinkTracker linkTracker, CosmosSink cosmosSink, IOutstandingWorkLatch latch)
        {
            LinkTracker = linkTracker;
            CosmosSink = cosmosSink;
            Latch = latch;
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

            // ADR-0022 slice 4: this Service Bus function IS the distributed
            // Crawl driver. ADR-0009: SpiderBuilder is internal; the bare
            // reduced shell comes from ScraperEngineBuilder.BuildSpider().
            var spider = new ScraperEngineBuilder()
                .WithLogger(log)
                .WithLinkTracker(LinkTracker)
                .BuildSpider();

            var children = ImmutableArray<Job>.Empty;

            // Idempotency authority (the atomic Redis SADD test-and-set,
            // ADR-0022 slice 2/3): a duplicate discovery or a redelivered
            // message loses the race and does NO work — no crawl, no fan-out,
            // no child enqueue. The shell returns a value and never throws to
            // terminate, so nothing poisons the queue (the pre-7.0
            // distributed poison message is gone by construction).
            if (await LinkTracker.TryAddVisitedLinkAsync(job.Url))
            {
                var report = await spider.CrawlAsync(job);

                if (report.Outcome is CrawlOutcome.Parsed parsed)
                    await CosmosSink.EmitAsync(parsed.Data);   // the driver fans out
                else
                    children = report.Outcome.NextJobs;        // unfiltered; dedup is the per-message gate above
            }
            else
            {
                log.LogInformation($"Duplicate/redelivered, no-op: {job.Url}");
            }

            if (children.Length > 0)
            {
                // Credit children BEFORE this message's unit is returned
                // (credit conservation — the latch can't hit zero early).
                await Latch.AddAsync(children.Length);

                foreach (var newJob in children)
                {
                    log.LogInformation($"Adding to the queue: {newJob.Url}");
                    await outputSbQueue.AddAsync(SerializeToJson(newJob));
                }
            }

            // Return this message's one unit of credit. The CAS-fenced latch
            // trips for exactly one caller when outstanding work hits zero —
            // the distributed "stop cleanly when work runs out" the design
            // required. Returning normally acks the message; nothing is ever
            // thrown to signal termination.
            if (await Latch.SignalProcessedAsync())
                log.LogInformation("Crawl complete: all work drained (distributed).");
        }
    }
}
