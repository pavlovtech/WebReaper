namespace WebReaper.Domain;

public record Job(
    string BaseUrl,
    string Url,
    LinkPathSelector[] LinkPathSelectors,
    PageType type = PageType.TransitPage,
    int DepthLevel = 0,
    int Priority = 0);
