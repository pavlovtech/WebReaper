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
- [IProxyProposalProvider](#T-WebReaper-Proxy-Abstract-IProxyProposalProvider 'WebReaper.Proxy.Abstract.IProxyProposalProvider')
  - [GetProxiesAsync()](#M-WebReaper-Proxy-Abstract-IProxyProposalProvider-GetProxiesAsync-System-Threading-CancellationToken- 'WebReaper.Proxy.Abstract.IProxyProposalProvider.GetProxiesAsync(System.Threading.CancellationToken)')
- [IProxyProposalValidator](#T-WebReaper-Proxy-Abstract-IProxyProposalValidator 'WebReaper.Proxy.Abstract.IProxyProposalValidator')
  - [ValidateAsync()](#M-WebReaper-Proxy-Abstract-IProxyProposalValidator-ValidateAsync-System-Net-WebProxy,System-Threading-CancellationToken- 'WebReaper.Proxy.Abstract.IProxyProposalValidator.ValidateAsync(System.Net.WebProxy,System.Threading.CancellationToken)')
- [IProxyProvider](#T-WebReaper-Proxy-Abstract-IProxyProvider 'WebReaper.Proxy.Abstract.IProxyProvider')
  - [GetProxyAsync()](#M-WebReaper-Proxy-Abstract-IProxyProvider-GetProxyAsync 'WebReaper.Proxy.Abstract.IProxyProvider.GetProxyAsync')
- [IValidatedProxyListProvider](#T-WebReaper-Proxy-Abstract-IValidatedProxyListProvider 'WebReaper.Proxy.Abstract.IValidatedProxyListProvider')
  - [GetProxiesAsync()](#M-WebReaper-Proxy-Abstract-IValidatedProxyListProvider-GetProxiesAsync-System-Threading-CancellationToken- 'WebReaper.Proxy.Abstract.IValidatedProxyListProvider.GetProxiesAsync(System.Threading.CancellationToken)')
- [InMemoryCookieStorage](#T-WebReaper-Core-CookieStorage-Concrete-InMemoryCookieStorage 'WebReaper.Core.CookieStorage.Concrete.InMemoryCookieStorage')
- [PingTimeoutProxyProposalValidator](#T-WebReaper-Proxy-Concrete-PingTimeoutProxyProposalValidator 'WebReaper.Proxy.Concrete.PingTimeoutProxyProposalValidator')
  - [#ctor()](#M-WebReaper-Proxy-Concrete-PingTimeoutProxyProposalValidator-#ctor-Microsoft-Extensions-Options-IOptions{WebReaper-Proxy-Concrete-PingTimeoutValidatorOptions}- 'WebReaper.Proxy.Concrete.PingTimeoutProxyProposalValidator.#ctor(Microsoft.Extensions.Options.IOptions{WebReaper.Proxy.Concrete.PingTimeoutValidatorOptions})')
  - [ValidateAsync()](#M-WebReaper-Proxy-Concrete-PingTimeoutProxyProposalValidator-ValidateAsync-System-Net-WebProxy,System-Threading-CancellationToken- 'WebReaper.Proxy.Concrete.PingTimeoutProxyProposalValidator.ValidateAsync(System.Net.WebProxy,System.Threading.CancellationToken)')
- [PingTimeoutValidatorOptions](#T-WebReaper-Proxy-Concrete-PingTimeoutValidatorOptions 'WebReaper.Proxy.Concrete.PingTimeoutValidatorOptions')
  - [ProbeTimeout](#P-WebReaper-Proxy-Concrete-PingTimeoutValidatorOptions-ProbeTimeout 'WebReaper.Proxy.Concrete.PingTimeoutValidatorOptions.ProbeTimeout')
  - [ProbeUrl](#P-WebReaper-Proxy-Concrete-PingTimeoutValidatorOptions-ProbeUrl 'WebReaper.Proxy.Concrete.PingTimeoutValidatorOptions.ProbeUrl')
- [ProxyProposalValidationResult](#T-WebReaper-Proxy-Concrete-ProxyProposalValidationResult 'WebReaper.Proxy.Concrete.ProxyProposalValidationResult')
  - [Default](#F-WebReaper-Proxy-Concrete-ProxyProposalValidationResult-Default 'WebReaper.Proxy.Concrete.ProxyProposalValidationResult.Default')
  - [Error](#P-WebReaper-Proxy-Concrete-ProxyProposalValidationResult-Error 'WebReaper.Proxy.Concrete.ProxyProposalValidationResult.Error')
  - [IsDefault](#P-WebReaper-Proxy-Concrete-ProxyProposalValidationResult-IsDefault 'WebReaper.Proxy.Concrete.ProxyProposalValidationResult.IsDefault')
  - [IsInvalid](#P-WebReaper-Proxy-Concrete-ProxyProposalValidationResult-IsInvalid 'WebReaper.Proxy.Concrete.ProxyProposalValidationResult.IsInvalid')
  - [IsValid](#P-WebReaper-Proxy-Concrete-ProxyProposalValidationResult-IsValid 'WebReaper.Proxy.Concrete.ProxyProposalValidationResult.IsValid')
  - [Invalid()](#M-WebReaper-Proxy-Concrete-ProxyProposalValidationResult-Invalid-System-Exception- 'WebReaper.Proxy.Concrete.ProxyProposalValidationResult.Invalid(System.Exception)')
  - [Valid()](#M-WebReaper-Proxy-Concrete-ProxyProposalValidationResult-Valid 'WebReaper.Proxy.Concrete.ProxyProposalValidationResult.Valid')
- [ProxyProposalValidatorService](#T-WebReaper-Proxy-Concrete-ProxyProposalValidatorService 'WebReaper.Proxy.Concrete.ProxyProposalValidatorService')
  - [#ctor()](#M-WebReaper-Proxy-Concrete-ProxyProposalValidatorService-#ctor-Microsoft-Extensions-Options-IOptions{WebReaper-Proxy-Concrete-ProxyProposalValidatorServiceOptions},Microsoft-Extensions-Logging-ILogger{WebReaper-Proxy-Concrete-ProxyProposalValidatorService},System-Collections-Generic-IEnumerable{WebReaper-Proxy-Abstract-IProxyProposalProvider},System-Collections-Generic-IEnumerable{WebReaper-Proxy-Abstract-IProxyProposalValidator}- 'WebReaper.Proxy.Concrete.ProxyProposalValidatorService.#ctor(Microsoft.Extensions.Options.IOptions{WebReaper.Proxy.Concrete.ProxyProposalValidatorServiceOptions},Microsoft.Extensions.Logging.ILogger{WebReaper.Proxy.Concrete.ProxyProposalValidatorService},System.Collections.Generic.IEnumerable{WebReaper.Proxy.Abstract.IProxyProposalProvider},System.Collections.Generic.IEnumerable{WebReaper.Proxy.Abstract.IProxyProposalValidator})')
  - [ExecuteAsync()](#M-WebReaper-Proxy-Concrete-ProxyProposalValidatorService-ExecuteAsync-System-Threading-CancellationToken- 'WebReaper.Proxy.Concrete.ProxyProposalValidatorService.ExecuteAsync(System.Threading.CancellationToken)')
  - [GetProxiesAsync()](#M-WebReaper-Proxy-Concrete-ProxyProposalValidatorService-GetProxiesAsync-System-Threading-CancellationToken- 'WebReaper.Proxy.Concrete.ProxyProposalValidatorService.GetProxiesAsync(System.Threading.CancellationToken)')
- [ProxyProposalValidatorServiceOptions](#T-WebReaper-Proxy-Concrete-ProxyProposalValidatorServiceOptions 'WebReaper.Proxy.Concrete.ProxyProposalValidatorServiceOptions')
  - [ValidationInterval](#P-WebReaper-Proxy-Concrete-ProxyProposalValidatorServiceOptions-ValidationInterval 'WebReaper.Proxy.Concrete.ProxyProposalValidatorServiceOptions.ValidationInterval')
- [ScraperEngineBuilder](#T-WebReaper-Builders-ScraperEngineBuilder 'WebReaper.Builders.ScraperEngineBuilder')
- [ValidatedProxyProvider](#T-WebReaper-Proxy-Concrete-ValidatedProxyProvider 'WebReaper.Proxy.Concrete.ValidatedProxyProvider')
  - [#ctor()](#M-WebReaper-Proxy-Concrete-ValidatedProxyProvider-#ctor-WebReaper-Proxy-Abstract-IValidatedProxyListProvider- 'WebReaper.Proxy.Concrete.ValidatedProxyProvider.#ctor(WebReaper.Proxy.Abstract.IValidatedProxyListProvider)')
  - [GetProxyAsync()](#M-WebReaper-Proxy-Concrete-ValidatedProxyProvider-GetProxyAsync-System-Threading-CancellationToken- 'WebReaper.Proxy.Concrete.ValidatedProxyProvider.GetProxyAsync(System.Threading.CancellationToken)')
  - [GetProxyAsync()](#M-WebReaper-Proxy-Concrete-ValidatedProxyProvider-GetProxyAsync 'WebReaper.Proxy.Concrete.ValidatedProxyProvider.GetProxyAsync')

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

<a name='T-WebReaper-Proxy-Abstract-IProxyProposalProvider'></a>
## IProxyProposalProvider `type`

##### Namespace

WebReaper.Proxy.Abstract

##### Summary

Supplies a list of unvalidated proxies.

<a name='M-WebReaper-Proxy-Abstract-IProxyProposalProvider-GetProxiesAsync-System-Threading-CancellationToken-'></a>
### GetProxiesAsync() `method`

##### Summary

Returns a list of potential proxies, which may or may not be valid.

##### Parameters

This method has no parameters.

<a name='T-WebReaper-Proxy-Abstract-IProxyProposalValidator'></a>
## IProxyProposalValidator `type`

##### Namespace

WebReaper.Proxy.Abstract

##### Summary

Validates a proposed proxy.

<a name='M-WebReaper-Proxy-Abstract-IProxyProposalValidator-ValidateAsync-System-Net-WebProxy,System-Threading-CancellationToken-'></a>
### ValidateAsync() `method`

##### Summary

Validates a proposed proxy.

##### Returns

A [ProxyProposalValidationResult](#T-WebReaper-Proxy-Concrete-ProxyProposalValidationResult 'WebReaper.Proxy.Concrete.ProxyProposalValidationResult') indicating whether the proxy is valid or invalid, or the validator does not apply to the result.

##### Parameters

This method has no parameters.

<a name='T-WebReaper-Proxy-Abstract-IProxyProvider'></a>
## IProxyProvider `type`

##### Namespace

WebReaper.Proxy.Abstract

##### Summary

Provides a validated proxy.

<a name='M-WebReaper-Proxy-Abstract-IProxyProvider-GetProxyAsync'></a>
### GetProxyAsync() `method`

##### Summary

Returns a validated proxy.

##### Parameters

This method has no parameters.

<a name='T-WebReaper-Proxy-Abstract-IValidatedProxyListProvider'></a>
## IValidatedProxyListProvider `type`

##### Namespace

WebReaper.Proxy.Abstract

##### Summary

Supplies a list of validated, ready to use proxies.

<a name='M-WebReaper-Proxy-Abstract-IValidatedProxyListProvider-GetProxiesAsync-System-Threading-CancellationToken-'></a>
### GetProxiesAsync() `method`

##### Summary

Returns a list of validated proxies.

##### Parameters

This method has no parameters.

<a name='T-WebReaper-Core-CookieStorage-Concrete-InMemoryCookieStorage'></a>
## InMemoryCookieStorage `type`

##### Namespace

WebReaper.Core.CookieStorage.Concrete

##### Summary

*Inherit from parent.*

<a name='T-WebReaper-Proxy-Concrete-PingTimeoutProxyProposalValidator'></a>
## PingTimeoutProxyProposalValidator `type`

##### Namespace

WebReaper.Proxy.Concrete

##### Summary

Validates a proxy by requesting a URL and waiting for a response.

<a name='M-WebReaper-Proxy-Concrete-PingTimeoutProxyProposalValidator-#ctor-Microsoft-Extensions-Options-IOptions{WebReaper-Proxy-Concrete-PingTimeoutValidatorOptions}-'></a>
### #ctor() `constructor`

##### Summary

Initializes a new instance of the [PingTimeoutProxyProposalValidator](#T-WebReaper-Proxy-Concrete-PingTimeoutProxyProposalValidator 'WebReaper.Proxy.Concrete.PingTimeoutProxyProposalValidator') class.

##### Parameters

This constructor has no parameters.

<a name='M-WebReaper-Proxy-Concrete-PingTimeoutProxyProposalValidator-ValidateAsync-System-Net-WebProxy,System-Threading-CancellationToken-'></a>
### ValidateAsync() `method`

##### Summary

*Inherit from parent.*

##### Parameters

This method has no parameters.

<a name='T-WebReaper-Proxy-Concrete-PingTimeoutValidatorOptions'></a>
## PingTimeoutValidatorOptions `type`

##### Namespace

WebReaper.Proxy.Concrete

##### Summary

Options for [PingTimeoutProxyProposalValidator](#T-WebReaper-Proxy-Concrete-PingTimeoutProxyProposalValidator 'WebReaper.Proxy.Concrete.PingTimeoutProxyProposalValidator').

<a name='P-WebReaper-Proxy-Concrete-PingTimeoutValidatorOptions-ProbeTimeout'></a>
### ProbeTimeout `property`

##### Summary

The maximum time to wait for a response from the probe URL.

<a name='P-WebReaper-Proxy-Concrete-PingTimeoutValidatorOptions-ProbeUrl'></a>
### ProbeUrl `property`

##### Summary

The URL to visit to validate the proxy.

<a name='T-WebReaper-Proxy-Concrete-ProxyProposalValidationResult'></a>
## ProxyProposalValidationResult `type`

##### Namespace

WebReaper.Proxy.Concrete

##### Summary

The result of validating a proxy.

##### Remarks

Either [IsValid](#P-WebReaper-Proxy-Concrete-ProxyProposalValidationResult-IsValid 'WebReaper.Proxy.Concrete.ProxyProposalValidationResult.IsValid') or [IsInvalid](#P-WebReaper-Proxy-Concrete-ProxyProposalValidationResult-IsInvalid 'WebReaper.Proxy.Concrete.ProxyProposalValidationResult.IsInvalid') will be `true` when initialized.

<a name='F-WebReaper-Proxy-Concrete-ProxyProposalValidationResult-Default'></a>
### Default `constants`

##### Summary

A default result.

<a name='P-WebReaper-Proxy-Concrete-ProxyProposalValidationResult-Error'></a>
### Error `property`

##### Summary

The error, if any.

<a name='P-WebReaper-Proxy-Concrete-ProxyProposalValidationResult-IsDefault'></a>
### IsDefault `property`

##### Summary

Whether the result is the default result.

<a name='P-WebReaper-Proxy-Concrete-ProxyProposalValidationResult-IsInvalid'></a>
### IsInvalid `property`

##### Summary

Whether the result is invalid.

<a name='P-WebReaper-Proxy-Concrete-ProxyProposalValidationResult-IsValid'></a>
### IsValid `property`

##### Summary

Whether the result is valid.

<a name='M-WebReaper-Proxy-Concrete-ProxyProposalValidationResult-Invalid-System-Exception-'></a>
### Invalid() `method`

##### Summary

An invalid result, with an error.

##### Parameters

This method has no parameters.

<a name='M-WebReaper-Proxy-Concrete-ProxyProposalValidationResult-Valid'></a>
### Valid() `method`

##### Summary

A valid result.

##### Parameters

This method has no parameters.

<a name='T-WebReaper-Proxy-Concrete-ProxyProposalValidatorService'></a>
## ProxyProposalValidatorService `type`

##### Namespace

WebReaper.Proxy.Concrete

##### Summary

Periodically validates proxies and supplies a the most recently validated list of proxies.

<a name='M-WebReaper-Proxy-Concrete-ProxyProposalValidatorService-#ctor-Microsoft-Extensions-Options-IOptions{WebReaper-Proxy-Concrete-ProxyProposalValidatorServiceOptions},Microsoft-Extensions-Logging-ILogger{WebReaper-Proxy-Concrete-ProxyProposalValidatorService},System-Collections-Generic-IEnumerable{WebReaper-Proxy-Abstract-IProxyProposalProvider},System-Collections-Generic-IEnumerable{WebReaper-Proxy-Abstract-IProxyProposalValidator}-'></a>
### #ctor() `constructor`

##### Summary

Periodically validates proxies and supplies a the most recently validated list of proxies.

##### Parameters

This constructor has no parameters.

<a name='M-WebReaper-Proxy-Concrete-ProxyProposalValidatorService-ExecuteAsync-System-Threading-CancellationToken-'></a>
### ExecuteAsync() `method`

##### Summary

*Inherit from parent.*

##### Parameters

This method has no parameters.

<a name='M-WebReaper-Proxy-Concrete-ProxyProposalValidatorService-GetProxiesAsync-System-Threading-CancellationToken-'></a>
### GetProxiesAsync() `method`

##### Summary

*Inherit from parent.*

##### Parameters

This method has no parameters.

<a name='T-WebReaper-Proxy-Concrete-ProxyProposalValidatorServiceOptions'></a>
## ProxyProposalValidatorServiceOptions `type`

##### Namespace

WebReaper.Proxy.Concrete

##### Summary

Options for [ProxyProposalValidatorService](#T-WebReaper-Proxy-Concrete-ProxyProposalValidatorService 'WebReaper.Proxy.Concrete.ProxyProposalValidatorService').

<a name='P-WebReaper-Proxy-Concrete-ProxyProposalValidatorServiceOptions-ValidationInterval'></a>
### ValidationInterval `property`

##### Summary

The interval at which to validate proxies.

<a name='T-WebReaper-Builders-ScraperEngineBuilder'></a>
## ScraperEngineBuilder `type`

##### Namespace

WebReaper.Builders

##### Summary

Builds a web scraper engine responsible for creating and receiving crawling jobs and running a spider on them

<a name='T-WebReaper-Proxy-Concrete-ValidatedProxyProvider'></a>
## ValidatedProxyProvider `type`

##### Namespace

WebReaper.Proxy.Concrete

##### Summary

Provides a random validated proxy.

##### See Also

- [WebReaper.Proxy.Concrete.ProxyProposalValidatorService](#T-WebReaper-Proxy-Concrete-ProxyProposalValidatorService 'WebReaper.Proxy.Concrete.ProxyProposalValidatorService')

<a name='M-WebReaper-Proxy-Concrete-ValidatedProxyProvider-#ctor-WebReaper-Proxy-Abstract-IValidatedProxyListProvider-'></a>
### #ctor() `constructor`

##### Summary

Initializes a new instance of the [ValidatedProxyProvider](#T-WebReaper-Proxy-Concrete-ValidatedProxyProvider 'WebReaper.Proxy.Concrete.ValidatedProxyProvider') class.

##### Parameters

This constructor has no parameters.

<a name='M-WebReaper-Proxy-Concrete-ValidatedProxyProvider-GetProxyAsync-System-Threading-CancellationToken-'></a>
### GetProxyAsync() `method`

##### Summary

*Inherit from parent.*

##### Parameters

This method has no parameters.

<a name='M-WebReaper-Proxy-Concrete-ValidatedProxyProvider-GetProxyAsync'></a>
### GetProxyAsync() `method`

##### Summary

*Inherit from parent.*

##### Parameters

This method has no parameters.
