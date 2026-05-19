using System.Collections.Immutable;
using WebReaper.Domain.PageActions;
using WebReaper.Domain.Parsing;
using WebReaper.Domain.Selectors;

namespace WebReaper.Domain;

/// <summary>
/// The immutable, serialisable crawl definition produced by
/// <see cref="WebReaper.Builders.ConfigBuilder.Build"/> and persisted to
/// <see cref="WebReaper.ConfigStorage.Abstract.IScraperConfigStorage"/> so the
/// Spider (in-process or a distributed worker) reads identical settings at
/// crawl time. Round-trips through <c>WebReaperJson</c> (ADR-0008).
/// </summary>
/// <param name="ParsingScheme">The extraction <see cref="Schema"/> for target
/// pages; null means no extraction.</param>
/// <param name="LinkPathSelectors">The selector chain that drives the crawl
/// state machine (ADR-0001).</param>
/// <param name="StartUrls">The seed URLs.</param>
/// <param name="UrlBlackList">URL patterns the crawl must never enqueue.</param>
/// <param name="PageCrawlLimit">Soft cap on pages crawled (ADR-0022:
/// best-effort, may overshoot by ~the parallelism degree).</param>
/// <param name="StartPageType">Whether the start URLs load static or
/// dynamic.</param>
/// <param name="PageActions">Browser actions for dynamic start pages.</param>
/// <param name="Headless">Run the browser headless for dynamic pages.</param>
/// <param name="StopWhenDrained">Stop once every discovered link is crawled
/// (issue #20 / ADR-0022 in-memory latch) instead of running forever.</param>
public record ScraperConfig(
    Schema? ParsingScheme,
    ImmutableQueue<LinkPathSelector> LinkPathSelectors,
    IEnumerable<string> StartUrls,
    IEnumerable<string> UrlBlackList,
    int PageCrawlLimit = int.MaxValue,
    PageType StartPageType = PageType.Static,
    List<PageAction>? PageActions = null,
    bool Headless = true,
    bool StopWhenDrained = false
);
