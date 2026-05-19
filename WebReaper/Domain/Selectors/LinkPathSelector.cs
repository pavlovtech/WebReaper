using WebReaper.Domain.PageActions;

namespace WebReaper.Domain.Selectors;

/// <summary>
/// One step in the crawl's selector chain (ADR-0001): the link selector to
/// follow from the current page, optionally a pagination selector (walk
/// next-page links applying <see cref="Selector"/> to each), and how the
/// reached pages load. The chain is consumed one step per crawl step; its
/// length is the state machine.
/// </summary>
/// <param name="Selector">The selector for the links to follow from the
/// current page.</param>
/// <param name="PaginationSelector">The next-page-link selector; null for a
/// plain follow step (no pagination).</param>
/// <param name="PageType">Whether the reached pages load static or
/// dynamic.</param>
/// <param name="PageActions">Browser actions for the reached dynamic
/// pages.</param>
public record LinkPathSelector(
    string Selector,
    string? PaginationSelector = null,
    PageType PageType = PageType.Static,
    List<PageAction>? PageActions = null)
{
    /// <summary>True when a <see cref="PaginationSelector"/> is set — this
    /// step paginates rather than plain-follows.</summary>
    public bool HasPagination => PaginationSelector != null;
}
