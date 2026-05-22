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
            // Crawl driver. ADR-0009/0025: SpiderBuilder is internal; the bare
            // reduced shell is built by DistributedSpiderBuilder. The link
            // tracker is the driver's idempotency authority (ADR-0022) — used
            // directly below, not wired into the spider shell.
            var spider = new DistributedSpiderBuilder()
                .WithLogger(log)
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

            // Register this message with the Outstanding-work latch BEFORE
            // enqueueing the discovered children: the latch credits the
            // children and returns this message's own unit in one atomic step
            // (credit conservation is structural — ADR-0032), so a fast worker
            // cannot draw the distributed counter to zero before a child's
            // credit exists.
            var concluded = await Latch.SignalProcessedAsync(children.Length);

            foreach (var newJob in children)
            {
                log.LogInformation($"Adding to the queue: {newJob.Url}");
                await outputSbQueue.AddAsync(SerializeToJson(newJob));
            }

            // The CAS-fenced latch trips for exactly one caller when
            // outstanding work hits zero — the distributed "stop cleanly when
            // work runs out". Returning normally acks the message; nothing is
            // ever thrown to signal termination.
            if (concluded)
                log.LogInformation("Crawl complete: all work drained (distributed).");
        }
    }
}
