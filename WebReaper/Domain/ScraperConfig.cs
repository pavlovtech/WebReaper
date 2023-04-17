using System.Collections.Immutable;
using WebReaper.Domain.PageActions;
using WebReaper.Domain.Parsing;
using WebReaper.Domain.Selectors;

namespace WebReaper.Domain;

public record ScraperConfig(
    Schema? ParsingScheme,
    ImmutableQueue<LinkPathSelector> LinkPathSelectors,
    IEnumerable<string> StartUrls,
    IEnumerable<string> UrlBlackList,
    int PageCrawlLimit = int.MaxValue,
    PageType StartPageType = PageType.Static,
    List<PageAction>? PageActions = null,
    bool Headless = true
);