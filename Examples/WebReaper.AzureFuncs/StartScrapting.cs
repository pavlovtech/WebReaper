using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using WebReaper.Domain;
using WebReaper.Domain.Parsing;
using WebReaper.Core;
using WebReaper.Scheduler.Concrete;

namespace WebReaper.AzureFuncs
{
    public class StartScrapting
    {
        private readonly ILogger<StartScrapting> _logger;
        private readonly AzureServiceBusScheduler _scheduler;

        public StartScrapting(ILogger<StartScrapting> log)
        {
            _logger = log;
            _scheduler = new AzureServiceBusScheduler("Endpoint=sb://webreaper.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=g0AAACe/NXS+/qWVad4KUnnw6iGECmUTJTpfFOMfjms=", "jobqueue"); //TODO: move to config
        }

        [FunctionName("StartScrapting")]
        [OpenApiOperation(operationId: "Run", tags: new[] { "name" })]
        [OpenApiParameter(name: "name", In = ParameterLocation.Query, Required = true, Type = typeof(string), Description = "The **Name** parameter")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "text/plain", bodyType: typeof(string), Description = "The OK response")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req)
        {
            _logger.LogInformation("C# HTTP trigger function processed a request.");

            var config = new ScraperConfigBuilder()
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

            await ScheduleFirstJobWithStartUrl(config);

            return new OkObjectResult(new
            {
                Message = "OK"
            });
        }

        private async Task ScheduleFirstJobWithStartUrl(ScraperConfig config)
        {
            await _scheduler.AddAsync(new Job(
            config.ParsingScheme!,
            config.StartUrl!,
            config.LinkPathSelectors));
        }
    }
}

