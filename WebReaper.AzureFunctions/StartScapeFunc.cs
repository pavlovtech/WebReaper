using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using WebReaper.Scraper;
using WebReaper.Domain.Parsing;
using WebReaper.Parsing;
using System.Collections.Immutable;
using WebReaper.Domain;
using WebReaper.Queue.AzureServiceBus;
using System.Linq;

namespace WebReaper.AzureFunctions
{
    public static class StartScapeFunc
    {
        [FunctionName("StartScapeFunc")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");

            //string name = req.Query["name"];

            // string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            // dynamic data = JsonConvert.DeserializeObject(requestBody);

            var config = new ScraperConfigBuilder()
                .WithLogger(log)
                .WithStartUrl("https://rutracker.org/forum/index.php?c=33")
                .FollowLinks("#cf-33 .forumlink>a")
                .FollowLinks(".forumlink>a")
                .FollowLinks("a.torTopic", ".pg")
                .WithScheme(new Schema 
                {
                    new("name", "#topic-title"),
                    new("category", "td.nav.t-breadcrumb-top.w100.pad_2>a:nth-child(3)"),
                    new("subcategory", "td.nav.t-breadcrumb-top.w100.pad_2>a:nth-child(5)"),
                    new("torrentSize", "div.attach_link.guest>ul>li:nth-child(2)"),
                    new Url("torrentLink", ".magnet-link"),
                    new Image("coverImageUrl", ".postImg")
                })
                .Build();

            var jobQueueWriter = new AzureJobQueueWriter("Endpoint=sb://webreaper.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=g0AAACe/NXS+/qWVad4KUnnw6iGECmUTJTpfFOMfjms=", "jobqueue");

            await jobQueueWriter.WriteAsync(new Job(
            config.ParsingScheme!,
            config.BaseUrl,
            config.StartUrl!,
            ImmutableQueue.Create(config.LinkPathSelectors.ToArray()),
            DepthLevel: 0));

            return new OkResult();
        }
    }
}
