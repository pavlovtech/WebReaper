using System.Collections.Immutable;
using Newtonsoft.Json;
using WebReaper.Domain.Parsing;
using WebReaper.Domain.Selectors;
using WebReaper.PageActions;

namespace WebReaper.ConfigStorage;

public record ScraperConfig(
    Schema? ParsingScheme,
    ImmutableQueue<LinkPathSelector> LinkPathSelectors,
    string StartUrl,
    IEnumerable<string> UrlBlackList,
    int PageCrawlLimit = int.MaxValue,
    PageType StartPageType = PageType.Static,
    List<PageAction>? PageActions = null
);
