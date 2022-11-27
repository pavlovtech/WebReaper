using System.Collections.Immutable;
using Exoscan.Domain.Parsing;
using Exoscan.Domain.Selectors;
using Exoscan.PageActions;
using Newtonsoft.Json;

namespace Exoscan.ConfigStorage;

public record ScraperConfig(
    Schema? ParsingScheme,
    ImmutableQueue<LinkPathSelector> LinkPathSelectors,
    string StartUrl,
    IEnumerable<string> UrlBlackList,
    int PageCrawlLimit = int.MaxValue,
    PageType StartPageType = PageType.Static,
    List<PageAction>? PageActions = null
);
