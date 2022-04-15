using Serilog;
using WebReaper;

Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console()
                .WriteTo.File("logs/log.txt", rollingInterval: RollingInterval.Day)
                .CreateLogger();

var watch = System.Diagnostics.Stopwatch.StartNew();

await new Scraper("https://rutracker.org/forum/viewforum.php?f=2327")
    .FollowLinks(".forumlink>a")
    .FollowLinks(".torTopic.bold.tt-text")
    .Paginate(".pg")
    .WithScheme(new WebEl[] {
        new("title", "span[style='font-size: 24px; line-height: normal;']"),
    })
    .Limit(100)
    .To("output.json")
    .Run();

watch.Stop();

Log.Logger.Information("Elapsed: {time} min", watch.Elapsed.TotalMinutes);
