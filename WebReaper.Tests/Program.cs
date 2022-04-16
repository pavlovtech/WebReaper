using Serilog;
using WebReaper;

Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console()
                .WriteTo.File("logs/log.txt", rollingInterval: RollingInterval.Day)
                .CreateLogger();

var watch = System.Diagnostics.Stopwatch.StartNew();

await new Scraper2("https://www.kniga.io/genres/page/1")
    .FollowLinks(".list-group-item>a")
    .FollowLinks("div.mb-2>h3>a")
    .WithScheme(new WebEl[] {
        new("title", "title")
    })
    .To("output.json")
    .Run();

watch.Stop();

Log.Logger.Information("Elapsed: {time} min", watch.Elapsed.TotalMinutes);
