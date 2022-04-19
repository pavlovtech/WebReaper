using WebReaper;
using WebReaper.Domain;

namespace ScraperWorkerService;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly Scraper scraper;

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;

        scraper = new Scraper("https://rutracker.org/forum/index.php?c=33")
            .FollowLinks("#cf-33 .forumlink>a")
            .FollowLinks(".forumlink>a")
            .FollowLinks("a.torTopic")
            .Paginate(".pg")
            .WithScheme(new WebEl[] {
                new("title", "title"),
                new("name", ".post_body>span"),
            })
            .To("output.json");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await scraper.Run();
    }
}

