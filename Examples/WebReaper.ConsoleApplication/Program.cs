using WebReaper.Builders;
using WebReaper.ProxyProviders.WebShareProxy;

_ = new ScraperEngineBuilder()
    .GetWithBrowser(
        new []{ "https://www.reddit.com/r/dotnet/" },
        actions => actions
        .ScrollToEnd()
        .RepeatWithDelay(10,2000)
        .Build())
    .FollowWithBrowser("a.SQnoC3ObvgnGjWt90zD9Z._2INHSNB8V5eaWp4P0rY_mE")
    .Parse(new()
    {
        new("title", "._eYtD2XCVieq6emjKBH3m"),
        new("text", "._3xX726aBn29LDbsDtzr_6E._1Ap4F5maDtT1E1YuCiaO0r.D3IL3FD0RFy_mkKLPwL4")
    })
    .WriteToJsonFile("output.json")
    .LogToConsole()
    .PageCrawlLimit(10)
    .HeadlessMode(false)
    .Build()
    .Run();

Console.ReadLine();