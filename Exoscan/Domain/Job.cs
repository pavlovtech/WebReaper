using System.Collections.Immutable;
using Exoscan.Domain.Selectors;
using Exoscan.PageActions;

namespace Exoscan.Domain;

public record Job(
    string Url,
    ImmutableQueue<LinkPathSelector> LinkPathSelectors,
    ImmutableQueue<string> ParentBacklinks,
    PageType PageType = PageType.Static,
    List<PageAction>? PageActions = null)
{
    public PageCategory PageCategory =>
        (LinkPathSelectors.Count(), LinkPathSelectors.FirstOrDefault()?.HasPagination) switch
        {
            (0, _) => PageCategory.TargetPage,
            (1, true) => PageCategory.PageWithPagination,
            _ => PageCategory.TransitPage
        };
};
