using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using WebReaper.Domain;
using WebReaper.Domain.Parsing;
using WebReaper.Parsing;
using WebReaper.Queue.AzureServiceBus;
using WebReaper.Scraper;

namespace WebReaper.AzureFunctions
{
    public class StartScrapting
    {
        private readonly ILogger _logger;

        public StartScrapting(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<StartScrapting>();
        }

        [Function("StartScrapting")]
        public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequestData req)
        {
            _logger.LogInformation("C# HTTP trigger function processed a request.");

            try
			{
				var config = new ScraperConfigBuilder()
					.WithLogger(_logger)
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
			}
			catch (System.Exception ex)
			{
				var response2 = req.CreateResponse(HttpStatusCode.OK);
				response2.Headers.Add("Content-Type", "text/plain; charset=utf-8");

				response2.WriteString(ex.ToString());

				return response2;
			}

			var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "text/plain; charset=utf-8");

            response.WriteString("Welcome to Azure Functions!");

            return response;
        }
    }
}
