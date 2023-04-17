<a name='assembly'></a>
# WebReaper

## Contents

- [BrowserPageLoader](#T-WebReaper-Core-Loaders-Abstract-BrowserPageLoader 'WebReaper.Core.Loaders.Abstract.BrowserPageLoader')
  - [#ctor(logger)](#M-WebReaper-Core-Loaders-Abstract-BrowserPageLoader-#ctor-Microsoft-Extensions-Logging-ILogger- 'WebReaper.Core.Loaders.Abstract.BrowserPageLoader.#ctor(Microsoft.Extensions.Logging.ILogger)')
  - [PageActions](#F-WebReaper-Core-Loaders-Abstract-BrowserPageLoader-PageActions 'WebReaper.Core.Loaders.Abstract.BrowserPageLoader.PageActions')
  - [Logger](#P-WebReaper-Core-Loaders-Abstract-BrowserPageLoader-Logger 'WebReaper.Core.Loaders.Abstract.BrowserPageLoader.Logger')
- [ConfigBuilder](#T-WebReaper-Builders-ConfigBuilder 'WebReaper.Builders.ConfigBuilder')
  - [Get(startUrls)](#M-WebReaper-Builders-ConfigBuilder-Get-System-String[]- 'WebReaper.Builders.ConfigBuilder.Get(System.String[])')
  - [GetWithBrowser(startUrls,pageActions)](#M-WebReaper-Builders-ConfigBuilder-GetWithBrowser-System-Collections-Generic-IEnumerable{System-String},System-Collections-Generic-List{WebReaper-Domain-PageActions-PageAction}- 'WebReaper.Builders.ConfigBuilder.GetWithBrowser(System.Collections.Generic.IEnumerable{System.String},System.Collections.Generic.List{WebReaper.Domain.PageActions.PageAction})')
- [FileScraperConfigStorage](#T-WebReaper-ConfigStorage-Concrete-FileScraperConfigStorage 'WebReaper.ConfigStorage.Concrete.FileScraperConfigStorage')
- [InMemoryCookieStorage](#T-WebReaper-Core-CookieStorage-Concrete-InMemoryCookieStorage 'WebReaper.Core.CookieStorage.Concrete.InMemoryCookieStorage')
- [ScraperEngineBuilder](#T-WebReaper-Builders-ScraperEngineBuilder 'WebReaper.Builders.ScraperEngineBuilder')

<a name='T-WebReaper-Core-Loaders-Abstract-BrowserPageLoader'></a>
## BrowserPageLoader `type`

##### Namespace

WebReaper.Core.Loaders.Abstract

##### Summary

Base class for implementing a browser page loader

<a name='M-WebReaper-Core-Loaders-Abstract-BrowserPageLoader-#ctor-Microsoft-Extensions-Logging-ILogger-'></a>
### #ctor(logger) `constructor`

##### Summary

Constructor that takes ILogger argument

##### Parameters

| Name | Type | Description |
| ---- | ---- | ----------- |
| logger | [Microsoft.Extensions.Logging.ILogger](#T-Microsoft-Extensions-Logging-ILogger 'Microsoft.Extensions.Logging.ILogger') |  |

<a name='F-WebReaper-Core-Loaders-Abstract-BrowserPageLoader-PageActions'></a>
### PageActions `constants`

##### Summary

Interactive browser actions that can be performed on the page

<a name='P-WebReaper-Core-Loaders-Abstract-BrowserPageLoader-Logger'></a>
### Logger `property`

##### Summary

Logger

<a name='T-WebReaper-Builders-ConfigBuilder'></a>
## ConfigBuilder `type`

##### Namespace

WebReaper.Builders

<a name='M-WebReaper-Builders-ConfigBuilder-Get-System-String[]-'></a>
### Get(startUrls) `method`

##### Summary

This method can be called only one time to specify urls to start crawling with.

##### Returns

instance of ConfigBuilder

##### Parameters

| Name | Type | Description |
| ---- | ---- | ----------- |
| startUrls | [System.String[]](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.String[] 'System.String[]') | Initial urls for crawling |

<a name='M-WebReaper-Builders-ConfigBuilder-GetWithBrowser-System-Collections-Generic-IEnumerable{System-String},System-Collections-Generic-List{WebReaper-Domain-PageActions-PageAction}-'></a>
### GetWithBrowser(startUrls,pageActions) `method`

##### Summary

This method can be called only one time to specify urls to start crawling with.

##### Returns

instance of ConfigBuilder

##### Parameters

| Name | Type | Description |
| ---- | ---- | ----------- |
| startUrls | [System.Collections.Generic.IEnumerable{System.String}](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Collections.Generic.IEnumerable 'System.Collections.Generic.IEnumerable{System.String}') | Initial urls for crawling |
| pageActions | [System.Collections.Generic.List{WebReaper.Domain.PageActions.PageAction}](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Collections.Generic.List 'System.Collections.Generic.List{WebReaper.Domain.PageActions.PageAction}') | Actions to perform on the page via a browser |

<a name='T-WebReaper-ConfigStorage-Concrete-FileScraperConfigStorage'></a>
## FileScraperConfigStorage `type`

##### Namespace

WebReaper.ConfigStorage.Concrete

##### Summary

*Inherit from parent.*

<a name='T-WebReaper-Core-CookieStorage-Concrete-InMemoryCookieStorage'></a>
## InMemoryCookieStorage `type`

##### Namespace

WebReaper.Core.CookieStorage.Concrete

##### Summary

*Inherit from parent.*

<a name='T-WebReaper-Builders-ScraperEngineBuilder'></a>
## ScraperEngineBuilder `type`

##### Namespace

WebReaper.Builders

##### Summary

Builds a web scraper engine responsible for creating and receiving crawling jobs and running a spider on them
