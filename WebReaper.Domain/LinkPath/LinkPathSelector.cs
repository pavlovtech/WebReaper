namespace WebReaper.Domain;

public record LinkPathSelector(
    string Selector,
    string? PaginationSelector = null,
    PageType PageType = PageType.Static,
    SelectorType SelectorType = SelectorType.Css) {
        public bool HasPagination => PaginationSelector != null;
    };
    

// public record LinkPathWithPaginationSelector(
//     string Selector,
//     SelectorType SelectorType = SelectorType.Css)
//     : LinkPathSelector(Selector, PageType.PageWithPagination, SelectorType);