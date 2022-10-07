using System.Collections.Immutable;
using WebReaper.Core.Domain.Parsing;
using WebReaper.Core.Domain.Selectors;

namespace WebReaper.Core.Domain;

public record Job(
    Schema Schema,
    string BaseUrl,
    string Url,
    ImmutableQueue<LinkPathSelector> LinkPathSelectors,
    PageType PageType = PageType.Static,
    string? Script = null)
{
    public PageCategory PageCategory =>
        (LinkPathSelectors.Count(), LinkPathSelectors.FirstOrDefault()?.HasPagination) switch
        {
            (0, _) => PageCategory.TargetPage,
            (1, true) => PageCategory.PageWithPagination,
            _ => PageCategory.TransitPage
        };
};
