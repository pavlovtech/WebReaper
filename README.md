# WebReaper

Declarative extensible web scraper written in C#. It visites the site links specified via FollowLinks method and when it reaches the target pages specified by the last FollowLinks call, then it parses pages and maps data to the Schema and saves it the the Sink(s).

Web scraper is a software that consits of two main parts:
1. Crawler that goes throuth site links
2. Parser that grabs the data you need from the web pages

This project has focused web crawlerd, which means that it doesn't visit all site links, only the ones that maches the selectors that you specify via the FollowLinks mothed.

This is work in progres! API is not stable and will change.

## Features:

1. Easy declarative parsing, e.g. get text by the .title css selector and save it in the title field:  new Schema { new("title", ".title") }.
2. Pagination support:  .FollowLinks("a.torTopic", ".pg") where .pg is pagination css selector.
3. Saving data to any sinks such as file, database or API. Saving to Json and CSV file is out of the box, you can add any custom provider by implementi the IScraperSink interface.
4. Distributed crawling support: provide your implementation of IJobQueueReader, IJobQueueWriter and ICrawledLinkTracker and run your crawler on ony cloud VM, serverless function, on-prem servers, etc.
5. Crowling and parsing Single Page Applications.

## Coming soon:

- [ ] Proxy support
- [ ] Azure functions for the distributed crawling
- [ ] Request throttling
- [ ] Autotune for parallelism degree and throttling
- [ ] Ports to NoneJS and GO


## Example:

```
await new Scraper()
    .WithLogger(logger)
    .WithStartUrl("https://rutracker.org/forum/index.php?c=33")
    .FollowLinks("#cf-33 .forumlink>a")
    .FollowLinks(".forumlink>a")
    .FollowLinks("a.torTopic", ".pg")
    .WithScheme(new Schema {
        new("name", "#topic-title"),
        new("category", "td.nav.t-breadcrumb-top.w100.pad_2>a:nth-child(3)"),
        new("subcategory", "td.nav.t-breadcrumb-top.w100.pad_2>a:nth-child(5)"),
        new("torrentSize", "div.attach_link.guest>ul>li:nth-child(2)"),
        new Url("torrentLink", ".magnet-link"),
        new Image("coverImageUrl", ".postImg")
    })
    .WithParallelismDegree(4)
    .WriteToJsonFile("result.json")
    .WriteToCsvFile("result.csv")
    .Build()
    .Run();
```
