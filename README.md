
# ![image](https://user-images.githubusercontent.com/6662454/167391357-edb02ce2-a63c-439b-be9b-69b4b4796b1c.png) WebReaper

[![NuGet](https://img.shields.io/nuget/v/WebReaper)](https://www.nuget.org/packages/WebReaper)

Please star this project if you find it useful!

## Overview

Declarative high performance web scraper in C#. Easily crawl any web site and parse the data, save structed result to a file, DB, etc.

## Install

```
dotnet add WebReaper
```

## Requirements

.NET 6

## Tech stack:

<p align="left">
  <img src="https://img.shields.io/badge/C%23-239120?style=for-the-badge&logo=c-sharp&logoColor=white" />
  <img src="https://img.shields.io/badge/Azure_Functions-0062AD?style=for-the-badge&logo=azure-functions&logoColor=white" />
  <img src="https://img.shields.io/badge/MongoDB-4EA94B?style=for-the-badge&logo=mongodb&logoColor=white" />
  <img src="https://img.shields.io/badge/redis-%23DD0031.svg?&style=for-the-badge&logo=redis&logoColor=white" />
  <img src="https://img.shields.io/badge/-Puppeteer-green?style=for-the-badge&logo=appveyor" />
</p>


:exclamation: This is work in progress! API is not stable and will change.

## 📋 Example:

```C#

var webReaper = new Scraper("reddit")
    .WithStartUrl("https://www.reddit.com/r/dotnet/")
    .FollowLinks("a.SQnoC3ObvgnGjWt90zD9Z._2INHSNB8V5eaWp4P0rY_mE")
    .Parse(new Schema
    {
        new("title", "._eYtD2XCVieq6emjKBH3m"),
        new("text", "._3xX726aBn29LDbsDtzr_6E._1Ap4F5maDtT1E1YuCiaO0r.D3IL3FD0RFy_mkKLPwL4")
    })
    .WriteToJsonFile("output.json")
    .WithLogger(new ColorConsoleLogger())
    .Run(10);

Console.ReadLine();
```

## Features:

* :zap: It's extremly fast due to parallelism and asynchrony
* 🗒 Declarative parsing with a structured scheme
* 💾 Saving data to any sinks such as JSON or CSV file, MongoDB, CosmosDB, Redis, etc.
* :earth_americas: Distributed crawling support: run your web scraper on ony cloud VMs, serverless functions, on-prem servers, etc.
* :octopus: Crowling and parsing Single Page Applications with Puppeteer
* 🖥 Proxy support
* 🌀 Automatic reties

## Usage examples

* Data mining
* Gathering data for machine learning
* Online price change monitoring and price comparison
* News aggregation
* Product review scraping (to watch the competition)
* Gathering real estate listings
* Tracking online presence and reputation
* Web mashup and web data integration
* MAP compliance
* Lead generation

## API overview

### SPA parsing example

Parsing single page applications is super simple, just specify PageType.Dynamic. In this case Puppeteer will be used to load the pages.

```C#
var webReaper = new Scraper("reddit")
    .WithStartUrl("https://www.reddit.com/r/dotnet/", PageType.Dynamic)
    .FollowLinks("a.SQnoC3ObvgnGjWt90zD9Z._2INHSNB8V5eaWp4P0rY_mE", PageType.Dynamic)
    .Parse(new Schema
    {
        new("title", "._eYtD2XCVieq6emjKBH3m"),
        new("text", "._3xX726aBn29LDbsDtzr_6E._1Ap4F5maDtT1E1YuCiaO0r.D3IL3FD0RFy_mkKLPwL4")
    })
    .WriteToJsonFile("output.json")
    .WithLogger(new ColorConsoleLogger())
    .Run(10);

Console.ReadLine();
```

Additionaly, you can run any JavaScript on dynamic pages as they are loaded with headless browser. In order to do that you need to pass the third parameter:

```C#
var webReaper = new Scraper("reddit")
    .WithStartUrl("https://www.reddit.com/r/dotnet/", PageType.Dynamic, "window.scrollTo(0, document.body.scrollHeight);")
    .FollowLinks("a.SQnoC3ObvgnGjWt90zD9Z._2INHSNB8V5eaWp4P0rY_mE", PageType.Dynamic, "window.scrollTo(0, document.body.scrollHeight);")
    .Parse(new Schema
    {
        new("title", "._eYtD2XCVieq6emjKBH3m"),
        new("text", "._3xX726aBn29LDbsDtzr_6E._1Ap4F5maDtT1E1YuCiaO0r.D3IL3FD0RFy_mkKLPwL4")
    })
    .WriteToJsonFile("output.json")
    .WithLogger(new ColorConsoleLogger())
    .Run(10);
```

It can be helpful if the required content is loaded only after some user interactions such as clicks, scrolls, etc.

### Authorization

If you need to pass authorization before parsing the web site, you can call Authorize method on Scraper that has to return CookieContainer with all cookies required for authorization. You are responsible for performing the login operation with your credentials, the Scraper only uses the cookies that you provide.

```C#
scraper = new Scraper()
    .WithLogger(logger)
    .WithStartUrl("https://rutracker.org/forum/index.php?c=33")
    .Authorize(() =>
    {
        var container = new CookieContainer();
        container.Add(new Cookie("AuthToken", "123");
        return container;
    })
```

### Distributed web scraping with Serverless approach

In the Examples folder you can find the project called WebReaper.AzureFuncs. It demonstrates the use of WebReaper with Azure Functions. It consists of two serverless functions:

#### StartScrapting
First of all, this function uses ScraperConfigBuilder to build the scraper configuration e. g.:

Secondly, this function writes the first web scraping job with startUrl to the Azure Service Bus queue:


#### WebReaperSpider

This Azure function is triggered by messages sent to the Azure Service Bus queue. Messages represent web scraping job. 

Firstly, this function builds the spider that is going to execute the job from the queue.

Secondly, it executes the job by loading the page, parsing content, saving to the database, etc.

Finally, it iterates through these new jobs and sends them the the Job queue.

### Extensibility

#### Adding a new sink to persist your data

Out of the box there are 4 sinks you can send your parsed data to: ConsoleSink, CsvFileSink, JsonFileSink, CosmosSink (Azure Cosmos database).

You can easly add your own by implementing the IScraperSink interface:

```C#
public interface IScraperSink
{
    public Task EmitAsync(JObject scrapedData);
}
```
Here is an example of the Console sink:

```C#
public class ConsoleSink : IScraperSink
{
    public Task EmitAsync(JObject scrapedData)
    {
        Console.WriteLine($"{scrapedData.ToString()}");
        return Task.CompletedTask;
    }
}
```
The scrapedData parameter is JSON object that contains scraped data that you specified in your schema.

Adding your sink to the Scraper is simple, just call AddSink method on the Scraper:

```C#
scraper = new Scraper()
    .AddSink(new ConsoleSink());
    .WithStartUrl("https://rutracker.org/forum/index.php?c=33")
    .FollowLinks("#cf-33 .forumlink>a")
    .FollowLinks(".forumlink>a")
    .FollowLinks("a.torTopic", ".pg")
    .Parse(new Schema {
        new("name", "#topic-title"),
    });
```

For other ways to extend your functionality see the next section.

### Intrefaces

| Interface           | Description                                                                                                                                               |
| ------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------- |
| IScheduler          | Reading and writing from the job queue. By default, the in-memory queue is used, but you can provider your implementation                                 |
| ICrawledLinkTracker | Tracker of visited links. A default implementation is an in-memory tracker. You can provide your own for Redis, MongoDB, etc.                             |
| IPageLoader         | Loader that takes URL and returns HTML of the page as a string                                                                                            |
| IContentParser      | Takes HTML and schema and returns JSON representation (JObject).                                                                                          |
| ILinkParser         | Takes HTML as a string and returns page links                                                                                                             |
| IScraperSink        | Represents a data store for writing the results of web scraping. Takes the JObject as parameter                                                           |
| ISpider             | A spider that does the crawling, parsing, and saving of the data                                                                                          |

### Main entities

* Job - a record that represends a job for the spider
* LinkPathSelector - represents a selector for links to be crawled

## Repository structure

| Project                                   | Description                                                                       |
| ----------------------------------------- | --------------------------------------------------------------------------------- |
| WebReaper                                 | Library for web scraping                                                          |
| WebReaper.ScraperWorkerService            | Example of using WebReaper library in a Worker Service .NET project.              |
| WebReaper.DistributedScraperWorkerService | Example of using WebReaper library in a distributed way wih Azure Service Bus     |
| WebReaper.AzureFuncs                      | Example of using WebReaper library with serverless approach using Azure Functions |
| WebReaper.ConsoleApplication              | Example of using WebReaper library with in a console application                  |


## Coming soon:

- [X] Nuget package
- [X] Azure functions for the distributed crawling
- [X] Parsing lists
- [X] Loading pages with headless browser and flexible SPA page manipulations (clicks, scrolls, etc)
- [X] Proxy support
- [ ] Add flexible conditions for ignoring or allowing certain pages
- [ ] Breadth first traversal with priprity channels
- [ ] Save auth cookies to redis
- [ ] Rest API example for web scraping
- [ ] Sitemap crawling support
- [ ] Ports to NodeJS and Go

## Features under consideration
- [ ] Saving logs to Seq
- [ ] Add LogTo method with Console and File support
- [ ] Site API support
- [ ] CRON for scheduling
- [ ] Request auto throttling
- [ ] Add bloom filter for revisiting same urls
- [ ] Improve architecture and refactor
- [ ] Subscribe to logs with lambda expression

See the [LICENSE](LICENSE.txt) file for license rights and limitations (GNU GPLv3).
