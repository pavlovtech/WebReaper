<a name='assembly'></a>
# WebReaper

## Contents

- [BrowserPageLoader](#T-WebReaper-Loaders-Abstract-BrowserPageLoader 'WebReaper.Loaders.Abstract.BrowserPageLoader')
  - [#ctor(logger)](#M-WebReaper-Loaders-Abstract-BrowserPageLoader-#ctor-Microsoft-Extensions-Logging-ILogger- 'WebReaper.Loaders.Abstract.BrowserPageLoader.#ctor(Microsoft.Extensions.Logging.ILogger)')
  - [PageActions](#F-WebReaper-Loaders-Abstract-BrowserPageLoader-PageActions 'WebReaper.Loaders.Abstract.BrowserPageLoader.PageActions')
  - [Logger](#P-WebReaper-Loaders-Abstract-BrowserPageLoader-Logger 'WebReaper.Loaders.Abstract.BrowserPageLoader.Logger')
- [FileScraperConfigStorage](#T-WebReaper-ConfigStorage-Concrete-FileScraperConfigStorage 'WebReaper.ConfigStorage.Concrete.FileScraperConfigStorage')
- [InMemoryCookieStorage](#T-WebReaper-CookieStorage-Concrete-InMemoryCookieStorage 'WebReaper.CookieStorage.Concrete.InMemoryCookieStorage')
- [ScraperEngineBuilder](#T-WebReaper-Builders-ScraperEngineBuilder 'WebReaper.Builders.ScraperEngineBuilder')

<a name='T-WebReaper-Loaders-Abstract-BrowserPageLoader'></a>
## BrowserPageLoader `type`

##### Namespace

WebReaper.Loaders.Abstract

##### Summary

Base class for implementing a browser page loader

<a name='M-WebReaper-Loaders-Abstract-BrowserPageLoader-#ctor-Microsoft-Extensions-Logging-ILogger-'></a>
### #ctor(logger) `constructor`

##### Summary

Constructor that takes ILogger argument

##### Parameters

| Name | Type | Description |
| ---- | ---- | ----------- |
| logger | [Microsoft.Extensions.Logging.ILogger](#T-Microsoft-Extensions-Logging-ILogger 'Microsoft.Extensions.Logging.ILogger') |  |

<a name='F-WebReaper-Loaders-Abstract-BrowserPageLoader-PageActions'></a>
### PageActions `constants`

##### Summary

Interactive browser actions that can be performed on the page

<a name='P-WebReaper-Loaders-Abstract-BrowserPageLoader-Logger'></a>
### Logger `property`

##### Summary

Logger

<a name='T-WebReaper-ConfigStorage-Concrete-FileScraperConfigStorage'></a>
## FileScraperConfigStorage `type`

##### Namespace

WebReaper.ConfigStorage.Concrete

##### Summary

*Inherit from parent.*

<a name='T-WebReaper-CookieStorage-Concrete-InMemoryCookieStorage'></a>
## InMemoryCookieStorage `type`

##### Namespace

WebReaper.CookieStorage.Concrete

##### Summary

*Inherit from parent.*

<a name='T-WebReaper-Builders-ScraperEngineBuilder'></a>
## ScraperEngineBuilder `type`

##### Namespace

WebReaper.Builders

##### Summary

Builds a web scraper engine responsible for creating and receiving crawling jobs and running a spider on them
