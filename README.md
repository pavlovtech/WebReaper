
# ![image](https://user-images.githubusercontent.com/6662454/167391357-edb02ce2-a63c-439b-be9b-69b4b4796b1c.png) WebReaper


Declarative extensible web scraper in C# with focused web crawler. Easly crawl any web site and parse the data, save structed result to a file, DB, etc.

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
        new Image("coverImageUrl", ".postImg")  // get a link to the image from HTML <img> tag (src attribute)
    })
    .WriteToJsonFile("result.json")
    .Build()
    .Run();
```

## Features:

* :zap: It's extremly fast due to parallelism and asynchrony
* 🗒 Declarative parsing with a structured scheme
* 💾 Saving data to any sinks such as file, database or API
* :earth_americas: Distributed crawling support: run your crawler on ony cloud VM, serverless function, on-prem servers, etc
* :octopus: Crowling and parsing Single Page Applications

## Extensibility

### Intrefaces

| Interface           | Description                                                                                                                                               |
| ------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------- |
| IJobQueueReader     | Reading from the job queue. By default, the in-memory queue is used, but you can provider your implementation for RabbitMQ, Azure Service Bus queue, etc. |
| IJobQueueWriter     | Writing to the job queue. By default, the in-memory queue is used, but you can provider your implementation for RabbitMQ, Azure Service Bus queue, etc.   |
| ICrawledLinkTracker | Tracker of visited links. A default implementation is an in-memory tracker. You can provide your own for Redis, MongoDB, etc.                             |
| IPageLoader         | Loader that takes URL and returns HTML of the page as a string                                                                                            |
| IContentParser      | Takes HTML and schema and returns JSON representation (JObject).                                                                                          |
| ILinkParser         | Takes HTML as a string and returns page links                                                                                                             |
| IScraperSink        | Represents a data store for writing the results of web scraping. Takes the JObject as parameter                                                           |
| ISpider             | A spider that does the crawling, parsing, and saving of the data                                                                                          |

### Main entities
* Job - a record that represends a job for the spider
* LinkPathSelector - represents a selector for links to be crawled
* PageCategory enum. Calculated automatically based on job's fields. Possible values:
    * TransitPage any page on the path to target page that you want to parse
    * PageWithPagination - page with pagination such as a catalog of goods, blog posts with pagination, etc
    * TargetPage - page that you want to scrape and save the result


## Coming soon:

- [ ] Proxy support
- [ ] Azure functions for the distributed crawling
- [ ] Request throttling
- [ ] Autotune for parallelism degree and throttling
- [ ] Flexible SPA manipulation
- [ ] Ports to NoneJS and GO

