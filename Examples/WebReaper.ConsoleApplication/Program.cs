using WebReaper.ConsoleApplication;
using WebReaper.Core;
using WebReaper.Core.Builders;
using WebReaper.Domain.Parsing;
using WebReaper.Domain.Selectors;
using WebReaper.ProxyProviders.WebShareProxy;

var engine = new ScraperEngineBuilder("rutracker")
    .Get("https://rutracker.org/forum/viewtopic.php?t=6273642")
    .Parse(new()
            {
                new("name", "#topic-title"),
                new("category", "td.nav.t-breadcrumb-top.w100.pad_2>a:nth-child(3)"),
                new("subcategory", "td.nav.t-breadcrumb-top.w100.pad_2>a:nth-child(5)"),
                new("torrentSize", "div.attach_link.guest>ul>li:nth-child(2)"),
                new("torrentLink", ".magnet-link", "href"),
                new("coverImageUrl", ".postImg", "src")
            })
    .WriteToJsonFile("output1.json")
    .WithLogger(new ColorConsoleLogger())
    .Build();

await engine.Run(1);

Console.ReadLine();