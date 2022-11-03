using WebReaper.Domain.Selectors;
using WebReaper.Domain.Parsing;
using System.Collections.Immutable;
using WebReaper.PageActions;

namespace WebReaper.Core;

public record ScraperConfig(
    Schema? ParsingScheme,
    ImmutableQueue<LinkPathSelector> LinkPathSelectors,
    string StartUrl,
    PageType StartPageType = PageType.Static,
    ImmutableQueue<PageAction>? PageActions = null
);
