# WebReaper.Puppeteer

Headless-browser (Puppeteer/Chromium) page-load transport for
[WebReaper](https://github.com/pavlovtech/WebReaper), for scraping
JavaScript-rendered pages.

Satellite package (ADR-0009): the headless-browser transport is kept out of
the WebReaper core so the core stays dependency-light and Native-AOT-clean.
The core is HTTP-only by default; **install this package and call
`.WithPuppeteerPageLoader()` to scrape Dynamic pages** (`CrawlWithBrowser` /
`FollowWithBrowser` / `PaginateWithBrowser`) — without it a Dynamic load
throws an actionable message. The first Dynamic run downloads Chromium.

## Install

```
dotnet add package WebReaper.Puppeteer
```

Pulls `WebReaper` (the core) as a dependency.

## Usage

Adds `WithPuppeteerPageLoader()` to `ScraperEngineBuilder`:

```csharp
using WebReaper.Builders;
using WebReaper.Puppeteer;

var engine = await ScraperEngineBuilder
    .CrawlWithBrowser("https://example.com/blog")
    .Extract(new() { new("title", "h1"), new("text", "article") })
    .WithPuppeteerPageLoader()
    .FollowWithBrowser(".post-link")
    .BuildAsync();

await engine.RunAsync();
```

## License

GPL-3.0-or-later. Part of the WebReaper project.
