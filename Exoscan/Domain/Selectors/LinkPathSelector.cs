using System.Collections.Immutable;
using Exoscan.PageActions;

namespace Exoscan.Domain.Selectors;

public record LinkPathSelector(
    string Selector,
    string? PaginationSelector = null,
    PageType PageType = PageType.Static,
    ImmutableQueue<PageAction>? PageActions = null)
{
    public bool HasPagination => PaginationSelector != null;
};