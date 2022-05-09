
# <img src="https://media.giphy.com/media/VgCDAzcKvsR6OM0uWg/giphy.gif" width="50"> WebReaper 
# <img src="https://media.giphy.com/media/OY9XK7PbFqkNO/giphy.gif" width="500"> Test

Declarative extensible web scraper in C# with focused web crawler. Crawl any site and parse any data, save structed result to file, DB, API, etc. 

:exclamation: This is work in progres! API is not stable and will change.

## 📋 Example:

```c#
await new Scraper()
    .WithStartUrl("https://rutracker.org/forum/index.php?c=33")
    .FollowLinks("#cf-33 .forumlink>a") // first level links
    .FollowLinks(".forumlink>a").       // second level links
    .FollowLinks("a.torTopic", ".pg").  // third level links to target pages
    .WithScheme(new Schema {
        new("name", "#topic-title"),
        new("category", "td.nav.t-breadcrumb-top.w100.pad_2>a:nth-child(3)"),
        new Url("torrentLink", ".magnet-link"), // get a link from <a> HTML tag (href attribute)
        new Image("coverImageUrl", ".postImg")  // gets a link to the image from HTML <img> tag (src attribute)
    })
    .WriteToJsonFile("result.json")
    .Build()
    .Run();
```

## Features:

* :zap: It's fast
* 🗒 Easy declarative parsing:  new Schema { new("field", ".selector") }
* :page_facing_up: Pagination support:  .FollowLinks("a", ".paginationSelector")
* Saving data to any sinks such as file, database or API
* :earth_americas: Distributed crawling support: provide your implementation of IJobQueueReader, IJobQueueWriter and ICrawledLinkTracker and run your crawler on ony cloud VM, serverless function, on-prem servers, etc
* :octopus: Crowling and parsing Single Page Applications

## Coming soon:

- [ ] Proxy support
- [ ] Azure functions for the distributed crawling
- [ ] Request throttling
- [ ] Autotune for parallelism degree and throttling
- [ ] Ports to NoneJS and GO

