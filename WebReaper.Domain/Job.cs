using System.Collections.Immutable;
using WebReaper.Domain.Parsing;
using WebReaper.Domain.Selectors;

namespace WebReaper.Domain;

public record Job(
    Schema schema,
    string BaseUrl,
    string Url,
    ImmutableQueue<LinkPathSelector> LinkPathSelectors,
    int DepthLevel = 0,
    PageType pageType = PageType.Static)
{
    public PageCategory PageCategory => 
        (LinkPathSelectors.Count(), LinkPathSelectors.FirstOrDefault()?.HasPagination) switch
        {
            (0, _) => PageCategory.TargetPage,
            (1, true) => PageCategory.PageWithPagination,
            _ => PageCategory.TransitPage
        };

    public int Priority => PageCategory switch
    {
        PageCategory.TargetPage => -int.MaxValue,
        PageCategory.PageWithPagination => -DepthLevel * (int)PageCategory.PageWithPagination,
        PageCategory.TransitPage => -DepthLevel * (int)PageCategory.TransitPage,
        PageCategory.Unknown => 0,
        _ => int.MaxValue
    };
};
