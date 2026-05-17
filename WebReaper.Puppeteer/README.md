# WebReaper.Puppeteer

Headless-browser (Puppeteer/Chromium) page-load transport for
[WebReaper](https://github.com/pavlovtech/WebReaper), for scraping
JavaScript-rendered pages.

Satellite package (ADR-0009): the headless-browser transport is kept out of
the WebReaper core so the core stays dependency-light and Native-AOT-clean.
The core is HTTP-only by default; **install this package and call
`.WithPuppeteerPageLoader()` to scrape Dynamic pages** (`GetWithBrowser` /
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

var engine = await new ScraperEngineBuilder()
    .WithPuppeteerPageLoader()
    .GetWithBrowser("https://example.com/blog")
    .FollowWithBrowser(".post-link")
    .Parse(new() { new("title", "h1"), new("text", "article") })
    .BuildAsync();

await engine.RunAsync();
```

## License

GPL-3.0-or-later. Part of the WebReaper project.
