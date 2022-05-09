using WebReaper.Domain.Selectors;
using WebReaper.Domain.Parsing;

namespace WebReaper.Scraper;

public record ScraperConfig(
    Schema ParsingScheme,
    LinkPathSelector[] LinkPathSelectors,
    string StartUrl,
    string? BaseUrl = null
);
