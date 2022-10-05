using System.Collections.Immutable;
using WebReaper.Domain.Parsing;
using WebReaper.Domain.Selectors;

namespace WebReaper.Domain;

public record Job(
    Schema Schema,
    string BaseUrl,
    string Url,
    ImmutableQueue<LinkPathSelector> LinkPathSelectors,
    int DepthLevel = 0,
    PageType PageType = PageType.Static)
{
    public PageCategory PageCategory => 
        (LinkPathSelectors.Count(), LinkPathSelectors.FirstOrDefault()?.HasPagination) switch
        {
            (0, _) => PageCategory.TargetPage,
            (1, true) => PageCategory.PageWithPagination,
            _ => PageCategory.TransitPage
        };
};
