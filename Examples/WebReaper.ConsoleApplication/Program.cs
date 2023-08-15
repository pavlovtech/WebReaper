using WebReaper.Builders;

var engine = await new ScraperEngineBuilder()
    .GetWithBrowser("https://www.alexpavlov.dev/blog")
    .FollowWithBrowser(".text-gray-900.transition")
    .Parse(new()
    {
        new("title", ".text-3xl.font-bold"),
        new("text", ".max-w-max.prose.prose-dark")
    })
    .WriteToJsonFile("output.json")
    .PageCrawlLimit(10)
    .WithParallelismDegree(30)
    .LogToConsole()
    .BuildAsync();

await engine.RunAsync();

Console.ReadLine();