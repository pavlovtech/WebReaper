![logo](https://user-images.githubusercontent.com/6662454/221978697-3f35564a-f442-46e6-9182-f2604a17e1f6.png)

# WebReaper

[![NuGet](https://img.shields.io/nuget/v/WebReaper)](https://www.nuget.org/packages/WebReaper)
[![build status](https://github.com/pavlovtech/WebReaper/actions/workflows/CI.yml/badge.svg)](https://github.com/pavlovtech/WebReaper/actions/workflows/CI.yml)

## Overview

WebReaper is a declarative high performance web scraper, crawler and parser in C#. Designed as simple, extensible and
scalable web scraping solution. Easily crawl any web site and parse the data, save structed result to a file, DB, or
pretty much to anywhere you want.

It provides a simple yet extensible API to make web scraping a breeze.

### 📋 Example:

![ray-so-export](https://user-images.githubusercontent.com/6662454/229387724-82ad04cb-6b90-42b8-ba2a-a3735fb94abe.png)

## Table of contents

- [Install](#install)
- [Requirements](#requirements)
- [Features](#features)
- [Usage examples](#usage-examples)
- [API overview](#api-overview)
  * [Parsing Single Page Applications](#parsing-single-page-applications)
  * [Persist the progress locally](#persist-the-progress-locally)
  * [Authorization](#authorization)
  * [How to disable headless mode](#how-to-disable-headless-mode)
  * [How to clean scraped data from the previous web scrapping run](#how-to-clean-scraped-data-from-the-previous-web-scrapping-run)
  * [How to clean visited links from the previous web scrapping run](#how-to-clean-visited-links-from-the-previous-web-scrapping-run)
  * [How to clean job queue from the previous web scraping run](#how-to-clean-job-queue-from-the-previous-web-scraping-run)
  * [Distributed web scraping with Serverless approach](#distributed-web-scraping-with-serverless-approach)
  * [Extensibility](#extensibility)
    + [Adding a new sink to persist your data](#adding-a-new-sink-to-persist-your-data)
  * [Intrefaces](#intrefaces)
  * [Main entities](#main-entities)
- [Repository structure](#repository-structure)

## Install

```
dotnet add package WebReaper
```

## Requirements

.NET 7

## Features

* :zap: High crawling speed due to parallelism and asynchrony
* 🗒 Declarative and easy to use
* 💾 Saving data to any data storages such as JSON or CSV file, MongoDB, CosmosDB, Redis, etc.
* :earth_americas: Scalable: run your web scraper on ony cloud VMs, serverless functions, on-prem
  servers, etc.
* :octopus: Crawling and parsing Single Page Applications with Puppeteer
* 🖥 Proxy support
* 🌀 Extensible: replace out-of-the-box implementations with your own

## Usage examples

* Data mining
* Gathering data for machine learning
* Online price change monitoring and price comparison
* News aggregation
* Product review scraping (to watch the competition)
* Tracking online presence and reputation

## API overview

### Parsing Single Page Applications

Parsing single page applications is super simple, just use the *GetWithBrowser* and/or *FollowWithBrowser* method. In this
case Puppeteer will be used to load the pages.

```C#
var engine = await new ScraperEngineBuilder()
    .GetWithBrowser("https://www.reddit.com/r/dotnet/")
    .Follow("a.SQnoC3ObvgnGjWt90zD9Z._2INHSNB8V5eaWp4P0rY_mE")
    .Parse(new()
    {
        new("title", "._eYtD2XCVieq6emjKBH3m"),
        new("text", "._3xX726aBn29LDbsDtzr_6E._1Ap4F5maDtT1E1YuCiaO0r.D3IL3FD0RFy_mkKLPwL4")
    })
    .WriteToJsonFile("output.json")
    .LogToConsole()
    .BuildAsync()

await engine.RunAsync();
```

Additionally, you can run any JavaScript on dynamic pages as they are loaded with headless browser. In order to do that
you need to add some page actions such as *.ScrollToEnd()*:

```C#
using WebReaper.Core.Builders;

var engine = await new ScraperEngineBuilder()
    .GetWithBrowser("https://www.reddit.com/r/dotnet/", actions => actions
        .ScrollToEnd()
        .Build())
    .Follow("a.SQnoC3ObvgnGjWt90zD9Z._2INHSNB8V5eaWp4P0rY_mE")
    .Parse(new()
    {
        new("title", "._eYtD2XCVieq6emjKBH3m"),
        new("text", "._3xX726aBn29LDbsDtzr_6E._1Ap4F5maDtT1E1YuCiaO0r.D3IL3FD0RFy_mkKLPwL4")
    })
    .WriteToJsonFile("output.json")
    .LogToConsole()
    .BuildAsync()

await engine.RunAsync();

Console.ReadLine();
```

It can be helpful if the required content is loaded only after some user interactions such as clicks, scrolls, etc.

### Persist the progress locally

If you want to persist the vistited links and job queue locally, so that you can start crawling where you left off you
can use *ScheduleWithTextFile* and *TrackVisitedLinksInFile* methods:

```C#
var engine = await new ScraperEngineBuilder()
    .WithLogger(logger)
    .Get("https://rutracker.org/forum/index.php?c=33")
    .Follow("#cf-33 .forumlink>a")
    .Follow(".forumlink>a")
    .Paginate("a.torTopic", ".pg")
    .Parse(new()
    {
	new("name", "#topic-title"),
	new("category", "td.nav.t-breadcrumb-top.w100.pad_2>a:nth-child(3)"),
	new("subcategory", "td.nav.t-breadcrumb-top.w100.pad_2>a:nth-child(5)"),
	new("torrentSize", "div.attach_link.guest>ul>li:nth-child(2)"),
	new("torrentLink", ".magnet-link", "href"),
	new("coverImageUrl", ".postImg", "src")
    })
    .WriteToJsonFile("result.json")
    .IgnoreUrls(blackList)
    .ScheduleWithTextFile("jobs.txt", "progress.txt")
    .TrackVisitedLinksInFile("links.txt")
    .BuildAsync();
```

### Authorization

If you need to pass authorization before parsing the web site, you can call SetCookies method on Scraper that has to
fill CookieContainer with all cookies required for authorization. You are responsible for performing the login operation
with your credentials, the Scraper only uses the cookies that you provide.

```C#
var engine = await new ScraperEngineBuilder()
    .WithLogger(logger)
    .Get("https://rutracker.org/forum/index.php?c=33")
    .SetCookies(cookies =>
    {
        cookies.Add(new Cookie("AuthToken", "123");
    })
    ...
```

### How to disable headless mode

If you scrape pages with a browser using GetWithBrowser and FollowWithBrowser methods, the default mode is headless
meaning that you won't see the browser during scraping. However, seeing the browser during scraping for debugging or
troubleshooting may be useful. To disable headless mode you the .HeadlessMode(false) method call.

```C#

var engine = await new ScraperEngineBuilder()
    .GetWithBrowser("https://www.reddit.com/r/dotnet/", actions => actions
        .ScrollToEnd()
        .Build())
    .HeadlessMode(false)
    ...
```

### How to clean scraped data from the previous web scrapping run

You may want to clean the data recived during the previous scraping to start you web scraping from scratch. In this case
use dataCleanupOnStart when adding a new sink:

```C#

var engine = await new ScraperEngineBuilder()
    .Get("https://www.reddit.com/r/dotnet/")
    .WriteToJsonFile("output.json", dataCleanupOnStart: true)
```

This dataCleanupOnStart parameter is present for all sinks, e.g. MongoDbSink, RedisSink, CosmosSink, etc.

### How to clean visited links from the previous web scrapping run

To clean up the list of visited links just pass true for dataCleanupOnStart parameter:

```C#
var engine = await new ScraperEngineBuilder()
    .Get("https://www.reddit.com/r/dotnet/")
    .TrackVisitedLinksInFile("visited.txt", dataCleanupOnStart: true)
```

### How to clean job queue from the previous web scraping run

Job queue is a queue of tasks schedules for web scraper. To clean up the job queue pass the dataCleanupOnStart parameter set to true.

```C#
var engine = await new ScraperEngineBuilder()
    .Get("https://www.reddit.com/r/dotnet/")
    .WithTextFileScheduler("jobs.txt", "currentJob.txt", dataCleanupOnStart: true)
```

### Distributed web scraping with Serverless approach

In the Examples folder you can find the project called WebReaper.AzureFuncs. It demonstrates the use of WebReaper with
Azure Functions. It consists of two serverless functions:

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

Out of the box there are 4 sinks you can send your parsed data to: ConsoleSink, CsvFileSink, JsonFileSink, CosmosSink (
Azure Cosmos database).

You can easily add your own by implementing the IScraperSink interface:

```C#
public interface IScraperSink
{
    public Task EmitAsync(ParsedData data);
}
```

Here is an example of the Console sink:

```C#
public class ConsoleSink : IScraperSink
{
    public Task EmitAsync(ParsedData parsedItam)
    {
        Console.WriteLine($"{parsedItam.Data.ToString()}");
        return Task.CompletedTask;
    }
}
```

Adding your sink to the Scraper is simple, just call *AddSink* method on the Scraper:

```C#
var engine = await new ScraperEngineBuilder()
    .AddSink(new ConsoleSink());
    .Get("https://rutracker.org/forum/index.php?c=33")
    .Follow("#cf-33 .forumlink>a")
    .Follow(".forumlink>a")
    .Paginate("a.torTopic", ".pg")
    .Parse(new() {
        new("name", "#topic-title"),
    })
    .BuildAsync();
```

For other ways to extend your functionality see the next section.

### Intrefaces

| Interface           | Description                                                                                                                   |
|---------------------|-------------------------------------------------------------------------------------------------------------------------------|
| IScheduler          | Reading and writing from the job queue. By default, the in-memory queue is used, but you can provider your implementation     |
| IVisitedLinkTracker | Tracker of visited links. A default implementation is an in-memory tracker. You can provide your own for Redis, MongoDB, etc. |
| IPageLoader         | Loader that takes URL and returns HTML of the page as a string                                                                |
| IContentParser      | Takes HTML and schema and returns JSON representation (JObject).                                                              |
| ILinkParser         | Takes HTML as a string and returns page links                                                                                 |
| IScraperSink        | Represents a data store for writing the results of web scraping. Takes the JObject as parameter                               |
| ISpider             | A spider that does the crawling, parsing, and saving of the data                                                              |

### Main entities

* Job - a record that represents a job for the spider
* LinkPathSelector - represents a selector for links to be crawled

## Repository structure

| Project                                   | Description                                                                       |
|-------------------------------------------|-----------------------------------------------------------------------------------|
| WebReaper                                 | Library for web scraping                                                          |
| WebReaper.ScraperWorkerService            | Example of using WebReaper library in a Worker Service .NET project.              |
| WebReaper.DistributedScraperWorkerService | Example of using WebReaper library in a distributed way wih Azure Service Bus     |
| WebReaper.AzureFuncs                      | Example of using WebReaper library with serverless approach using Azure Functions |
| WebReaper.ConsoleApplication              | Example of using WebReaper library with in a console application                  |

See the [LICENSE](LICENSE.txt) file for license rights and limitations (GNU GPLv3).
