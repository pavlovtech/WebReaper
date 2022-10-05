namespace WebReaper.Domain.Selectors;

public record LinkPathSelector(
    string Selector,
    string? PaginationSelector = null,
    PageType PageType = PageType.Static,
    SelectorType SelectorType = SelectorType.Css) {
        public bool HasPagination => PaginationSelector != null;
    };