namespace WebReaper.Domain.Selectors;

public record LinkPathSelector(
    string Selector,
    string? PaginationSelector = null,
    PageType PageType = PageType.Static,
    string? ScriptExpression = null)
{
    public bool HasPagination => PaginationSelector != null;
};