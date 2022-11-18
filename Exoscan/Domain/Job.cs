using System.Collections.Immutable;
using Exoscan.Domain.Parsing;
using Exoscan.Domain.Selectors;
using Exoscan.PageActions;

namespace Exoscan.Domain;

public record Job(
    Schema? Schema,
    string Url,
    ImmutableQueue<LinkPathSelector> LinkPathSelectors,
    PageType PageType = PageType.Static,
    ImmutableQueue<PageAction>? PageActions = null)
{
    public PageCategory PageCategory =>
        (LinkPathSelectors.Count(), LinkPathSelectors.FirstOrDefault()?.HasPagination) switch
        {
            (0, _) => PageCategory.TargetPage,
            (1, true) => PageCategory.PageWithPagination,
            _ => PageCategory.TransitPage
        };
};
