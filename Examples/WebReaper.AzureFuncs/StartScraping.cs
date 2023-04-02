using System.Collections.Immutable;
using System.Net;
using System.Threading.Tasks;
using WebReaper.ConfigStorage;
using WebReaper.ConfigStorage.Abstract;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using WebReaper.Builders;
using WebReaper.Domain;
using WebReaper.Domain.Parsing;
using WebReaper.Core;
using WebReaper.Core.Scheduler.Concrete;

namespace WebReaper.AzureFuncs
{
    public class StartScraping
    {
        private readonly IScraperConfigStorage _configStorage;
        private readonly ILogger<StartScraping> _logger;
        private readonly AzureServiceBusScheduler _scheduler;

        public StartScraping(IScraperConfigStorage configStorage, ILogger<StartScraping> log)
        {
            _configStorage = configStorage;
            _logger = log;
            _scheduler = new AzureServiceBusScheduler("", "jobqueue"); //TODO: move to config
        }

        [FunctionName("StartScraping")]
        [OpenApiOperation(operationId: "Run", tags: new[] { "name" })]
        [OpenApiParameter(name: "name", In = ParameterLocation.Query, Required = true, Type = typeof(string), Description = "The **Name** parameter")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "text/plain", bodyType: typeof(string), Description = "The OK response")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req)
        {
            _logger.LogInformation("C# HTTP trigger function processed a request.");

            var blackList = new string[] {
                "https://rutracker.org/forum/viewforum.php?f=396",
                "https://rutracker.org/forum/viewforum.php?f=2322",
                "https://rutracker.org/forum/viewforum.php?f=1993",
                "https://rutracker.org/forum/viewforum.php?f=2167",
                "https://rutracker.org/forum/viewforum.php?f=2321"
            };
            
            var config = new ConfigBuilder()
                .Get("https://rutracker.org/forum/index.php?c=33")
                .Follow("#cf-33 .forumlink>a")
                .Follow(".forumlink>a")
                .Paginate("a.torTopic", ".pg")
                .WithScheme(new()
                {
                        new("name", "#topic-title"),
                        new("category", "td.nav.t-breadcrumb-top.w100.pad_2>a:nth-child(3)"),
                        new("subcategory", "td.nav.t-breadcrumb-top.w100.pad_2>a:nth-child(5)"),
                        new("torrentSize", "div.attach_link.guest>ul>li:nth-child(2)"),
                        new("torrentLink", ".magnet-link", "href"),
                        new("coverImageUrl", ".postImg", "src")
                })
                .IgnoreUrls(blackList)
                .Build();

            await _configStorage.CreateConfigAsync(config);

            await ScheduleFirstJobsWithStartUrls(config);

            return new OkObjectResult(new
            {
                Message = "OK"
            });
        }

        private async Task ScheduleFirstJobsWithStartUrls(ScraperConfig config)
        {
            foreach (var url in config.StartUrls)
            {
                await _scheduler.AddAsync(
                    new Job(url, config.LinkPathSelectors,
                        ImmutableQueue.Create<string>()));
            }
        }
    }
}

