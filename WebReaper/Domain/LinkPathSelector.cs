namespace WebReaper.Domain;

public record LinkPathSelector(
    string Selector,
    string? PaginationSelector = null,
    PageType PageType = PageType.TransitPage,
    SelectorType SelectorType = SelectorType.Css);

// public record LinkPathWithPaginationSelector(
//     string Selector,
//     SelectorType SelectorType = SelectorType.Css)
//     : LinkPathSelector(Selector, PageType.PageWithPagination, SelectorType);