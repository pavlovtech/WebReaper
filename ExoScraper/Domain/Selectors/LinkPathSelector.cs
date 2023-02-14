using System.Collections.Immutable;
using ExoScraper.PageActions;

namespace ExoScraper.Domain.Selectors;

public record LinkPathSelector(
    string Selector,
    string? PaginationSelector = null,
    PageType PageType = PageType.Static,
    List<PageAction>? PageActions = null)
{
    public bool HasPagination => PaginationSelector != null;
};