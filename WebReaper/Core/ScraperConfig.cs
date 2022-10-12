using WebReaper.Domain.Selectors;
using WebReaper.Domain.Parsing;
using System.Collections.Immutable;

namespace WebReaper.Core;

public record ScraperConfig(
    Schema ParsingScheme,
    ImmutableQueue<LinkPathSelector> LinkPathSelectors,
    string StartUrl,
    PageType StartPageType = PageType.Static,
    string? initialScript = null
);
