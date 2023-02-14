using System.Collections.Immutable;
using ExoScraper.Domain.Parsing;
using ExoScraper.Domain.Selectors;
using ExoScraper.PageActions;
using Newtonsoft.Json;

namespace ExoScraper.ConfigStorage;

public record ScraperConfig(
    Schema? ParsingScheme,
    ImmutableQueue<LinkPathSelector> LinkPathSelectors,
    string StartUrl,
    IEnumerable<string> UrlBlackList,
    int PageCrawlLimit = int.MaxValue,
    PageType StartPageType = PageType.Static,
    List<PageAction>? PageActions = null
);
