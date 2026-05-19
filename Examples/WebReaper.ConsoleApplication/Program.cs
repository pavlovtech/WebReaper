using WebReaper.Builders;
using WebReaper.Puppeteer;

var engine = await ScraperEngineBuilder
    .CrawlWithBrowser("https://www.alexpavlov.dev/blog")
    .Extract(new()
    {
        new("title", ".text-3xl.font-bold"),
        new("text", ".max-w-max.prose.prose-dark")
    })
    .WithPuppeteerPageLoader()
    .FollowWithBrowser(".text-gray-900.transition")
    .WriteToJsonFile("output.json")
    .PageCrawlLimit(10)
    .WithParallelismDegree(30)
    .LogToConsole()
    .BuildAsync();

await engine.RunAsync();

Console.ReadLine();