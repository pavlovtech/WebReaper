using System.Collections.Immutable;
using WebReaper.Domain.Parsing;
using WebReaper.Domain.Selectors;
using WebReaper.PageActions;

namespace WebReaper.Domain;

public record Job(
    string SiteId,
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
