using WebReaper.Core.Domain.Selectors;
using WebReaper.Core.Domain.Parsing;

namespace WebReaper.Core.Scraper;

public record ScraperConfig(
    Schema ParsingScheme,
    LinkPathSelector[] LinkPathSelectors,
    string StartUrl,
    PageType StartPageType = PageType.Static,
    string? initialScript = null,
    string? BaseUrl = null
);
