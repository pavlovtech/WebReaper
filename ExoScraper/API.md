<a name='assembly'></a>
# ExoScraper

## Contents

- [BrowserPageLoader](#T-ExoScraper-Loaders-Abstract-BrowserPageLoader 'ExoScraper.Loaders.Abstract.BrowserPageLoader')
  - [#ctor(logger)](#M-ExoScraper-Loaders-Abstract-BrowserPageLoader-#ctor-Microsoft-Extensions-Logging-ILogger- 'ExoScraper.Loaders.Abstract.BrowserPageLoader.#ctor(Microsoft.Extensions.Logging.ILogger)')
  - [PageActions](#F-ExoScraper-Loaders-Abstract-BrowserPageLoader-PageActions 'ExoScraper.Loaders.Abstract.BrowserPageLoader.PageActions')
  - [Logger](#P-ExoScraper-Loaders-Abstract-BrowserPageLoader-Logger 'ExoScraper.Loaders.Abstract.BrowserPageLoader.Logger')
- [FileScraperConfigStorage](#T-ExoScraper-ConfigStorage-Concrete-FileScraperConfigStorage 'ExoScraper.ConfigStorage.Concrete.FileScraperConfigStorage')
- [InMemoryCookieStorage](#T-ExoScraper-CookieStorage-Concrete-InMemoryCookieStorage 'ExoScraper.CookieStorage.Concrete.InMemoryCookieStorage')
- [ScraperEngineBuilder](#T-ExoScraper-Core-Builders-ScraperEngineBuilder 'ExoScraper.Core.Builders.ScraperEngineBuilder')

<a name='T-ExoScraper-Loaders-Abstract-BrowserPageLoader'></a>
## BrowserPageLoader `type`

##### Namespace

ExoScraper.Loaders.Abstract

##### Summary

Base class for implementing a browser page loader

<a name='M-ExoScraper-Loaders-Abstract-BrowserPageLoader-#ctor-Microsoft-Extensions-Logging-ILogger-'></a>
### #ctor(logger) `constructor`

##### Summary

Constructor that takes ILogger argument

##### Parameters

| Name | Type | Description |
| ---- | ---- | ----------- |
| logger | [Microsoft.Extensions.Logging.ILogger](#T-Microsoft-Extensions-Logging-ILogger 'Microsoft.Extensions.Logging.ILogger') |  |

<a name='F-ExoScraper-Loaders-Abstract-BrowserPageLoader-PageActions'></a>
### PageActions `constants`

##### Summary

Interactive browser actions that can be performed on the page

<a name='P-ExoScraper-Loaders-Abstract-BrowserPageLoader-Logger'></a>
### Logger `property`

##### Summary

Logger

<a name='T-ExoScraper-ConfigStorage-Concrete-FileScraperConfigStorage'></a>
## FileScraperConfigStorage `type`

##### Namespace

ExoScraper.ConfigStorage.Concrete

##### Summary

*Inherit from parent.*

<a name='T-ExoScraper-CookieStorage-Concrete-InMemoryCookieStorage'></a>
## InMemoryCookieStorage `type`

##### Namespace

ExoScraper.CookieStorage.Concrete

##### Summary

*Inherit from parent.*

<a name='T-ExoScraper-Core-Builders-ScraperEngineBuilder'></a>
## ScraperEngineBuilder `type`

##### Namespace

ExoScraper.Core.Builders

##### Summary

Builds a web scraper engine responsible for creating and receiving crawling jobs and running a spider on them
