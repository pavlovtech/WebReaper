using System.Collections.Immutable;
using WebReaper.Domain.Selectors;

namespace WebReaper.Domain;

public record Job(
    SchemaElement[] schema,
    string BaseUrl,
    string Url,
    ImmutableQueue<LinkPathSelector> LinkPathSelectors,
    int DepthLevel = 0)
{
    public PageCategory PageCategory { get {
        if(!LinkPathSelectors.Any()) {
            return PageCategory.TargetPage;
        }

        var withPagination = LinkPathSelectors.Peek().HasPagination;

        if(LinkPathSelectors.Count() == 1 && withPagination) {
            return PageCategory.PageWithPagination;
        }

        return PageCategory.TransitPage;
    }}

    public int Priority => PageCategory switch
    {
        PageCategory.TargetPage => -int.MaxValue,
        PageCategory.PageWithPagination => -DepthLevel * (int)PageCategory.PageWithPagination,
        PageCategory.TransitPage => -DepthLevel * (int)PageCategory.TransitPage,
        PageCategory.Unknown => 0,
        _ => int.MaxValue
    };
};
