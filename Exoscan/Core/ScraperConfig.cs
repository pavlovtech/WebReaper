using System.Collections.Immutable;
using Exoscan.Domain.Parsing;
using Exoscan.Domain.Selectors;
using Exoscan.PageActions;

namespace Exoscan.Core;

public record ScraperConfig(
    Schema? ParsingScheme,
    ImmutableQueue<LinkPathSelector> LinkPathSelectors,
    string StartUrl,
    PageType StartPageType = PageType.Static,
    List<PageAction>? PageActions = null
);
