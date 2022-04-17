using Serilog;
using WebReaper;

Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.Console()
                .WriteTo.File("logs/log.txt", rollingInterval: RollingInterval.Day)
                .CreateLogger();

var watch = System.Diagnostics.Stopwatch.StartNew();

await new Scraper2("https://rutracker.org/forum/index.php?c=33")
    .FollowLinks("#cf-33 .forumlink>a")
    .FollowLinks(".forumlink>a")
    .FollowLinks("a.torTopic")
    .Paginate(".pg")
    .WithScheme(new WebEl[] {
        new("title", "title"),
        new("name", ".post_body>span"),
    })
    .To("output.json")
    .Run();

watch.Stop();

Log.Logger.Information("Elapsed: {time} min", watch.Elapsed.TotalMinutes);
