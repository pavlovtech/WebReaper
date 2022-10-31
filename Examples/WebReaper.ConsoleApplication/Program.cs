using WebReaper.ConsoleApplication;
using WebReaper.Core.Builders;
using WebReaper.ProxyProviders.WebShareProxy;

var engine = new ScraperEngineBuilder("reddit")
    .GetWithBrowser("https://www.reddit.com/r/dotnet/")
    .Follow("a.SQnoC3ObvgnGjWt90zD9Z._2INHSNB8V5eaWp4P0rY_mE")
    .Parse(new()
    {
        new("title", "._eYtD2XCVieq6emjKBH3m"),
        new("text", "._3xX726aBn29LDbsDtzr_6E._1Ap4F5maDtT1E1YuCiaO0r.D3IL3FD0RFy_mkKLPwL4")
    })
    .WriteToJsonFile("output.json")
    .WithProxies(new WebShareProxyProvider())
    .WithLogger(new ColorConsoleLogger())
    .Build();

await engine.Run(1);

Console.ReadLine();