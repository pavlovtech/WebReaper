using System.Collections;
using System.Collections.Immutable;
using WebReaper.Domain.Parsing;
using WebReaper.Domain.Selectors;
using WebReaper.PageActions;

namespace WebReaper.Domain;

public record ScraperConfig(
    Schema? ParsingScheme,
    ImmutableQueue<LinkPathSelector> LinkPathSelectors,
    IEnumerable<string> StartUrls,
    IEnumerable<string> UrlBlackList,
    int PageCrawlLimit = int.MaxValue,
    PageType StartPageType = PageType.Static,
    List<PageAction>? PageActions = null
);
