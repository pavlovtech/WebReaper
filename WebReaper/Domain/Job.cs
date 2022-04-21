namespace WebReaper.Domain;

public record Job(
    string BaseUrl,
    string Url,
    string[] LinkPathSelectors,
    string? PaginationSelector,
    PageType type = PageType.TransitPage,
    int DepthLevel = 0,
    int Priority = 0);
