using WebReaper.Domain.PageActions;

namespace WebReaper.Domain.Selectors;

/// <summary>
/// One step in the crawl's selector chain (ADR-0001): the link selector to
/// follow from the current page, optionally a pagination selector (walk
/// next-page links applying <see cref="Selector"/> to each), and how the
/// reached pages load. The chain is consumed one step per crawl step; its
/// length is the state machine.
///
/// The grammar is enforced at the construction site (ADR-0030): the primary
/// constructor rejects an empty <see cref="Selector"/>, an empty (but
/// non-null) <see cref="PaginationSelector"/>, and <see cref="PageActions"/>
/// carrying actions when <see cref="PageType"/> is
/// <see cref="PageType.Static"/> — the static HTTP transport ignores them, so
/// that pairing is a silent feature-drop. <see cref="Follow"/> and
/// <see cref="Paginate"/> are the named factories for the two intent-shapes,
/// the sibling pair to <c>Schema.ListOf</c> (ADR-0028).
/// </summary>
/// <param name="Selector">The selector for the links to follow from the
/// current page; non-empty.</param>
/// <param name="PaginationSelector">The next-page-link selector; null for a
/// plain follow step (no pagination), non-empty otherwise.</param>
/// <param name="PageType">Whether the reached pages load static or
/// dynamic.</param>
/// <param name="PageActions">Browser actions for the reached dynamic
/// pages; permitted only with <see cref="PageType.Dynamic"/>.</param>
public record LinkPathSelector(
    string Selector,
    string? PaginationSelector = null,
    PageType PageType = PageType.Static,
    List<PageAction>? PageActions = null)
{
    /// <summary>The selector for the links to follow from the current page.
    /// Non-empty — rejected at construction otherwise (ADR-0030).</summary>
    public string Selector { get; init; } = NonEmptySelector(Selector);

    /// <summary>The next-page-link selector; null for a plain follow step
    /// (no pagination). When non-null it must be non-empty (ADR-0030).</summary>
    public string? PaginationSelector { get; init; } =
        NonEmptyPaginationSelector(PaginationSelector);

    /// <summary>Browser actions for the reached dynamic pages. Permitted only
    /// with <see cref="PageType.Dynamic"/>: a non-empty list paired with
    /// <see cref="PageType.Static"/> is rejected at construction, since the
    /// static HTTP transport silently ignores them (ADR-0030).</summary>
    public List<PageAction>? PageActions { get; init; } =
        ActionsMatchTransport(PageActions, PageType);

    /// <summary>
    /// True when this is a recursive Site-sweep step (ADR-0081): the Crawl
    /// step returns the <c>Swept</c> arm: extract this page <em>and</em>
    /// follow its on-domain links, the child Jobs <b>retaining</b> this
    /// selector so the traversal perpetuates until the Visited-link tracker
    /// frontier saturates. Set only by <see cref="Sweep"/>; false for every
    /// other construction path, so the advance/retain behaviour of
    /// <see cref="Follow"/> / <see cref="Paginate"/> is unchanged. The
    /// on-domain boundary and the page/depth caps are crawl-global
    /// (<see cref="WebReaper.Domain.ScraperConfig"/>), not part of this
    /// per-step intent.
    /// </summary>
    public bool Recursive { get; init; }

    /// <summary>True when a <see cref="PaginationSelector"/> is set — this
    /// step paginates rather than plain-follows.</summary>
    public bool HasPagination => PaginationSelector != null;

    /// <summary>
    /// Plain-follow step: from the current page, enqueue child Jobs for the
    /// links matching <paramref name="selector"/>; no pagination. The named
    /// factory for the follow intent-shape (ADR-0030).
    /// </summary>
    /// <param name="selector">The links to follow; non-empty.</param>
    /// <param name="pageType">How the reached pages load.</param>
    /// <param name="actions">Browser actions for the reached pages; allowed
    /// only with <see cref="PageType.Dynamic"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="selector"/> is
    /// null/empty/whitespace, or <paramref name="actions"/> is non-empty with
    /// <see cref="PageType.Static"/>.</exception>
    public static LinkPathSelector Follow(
        string selector,
        PageType pageType = PageType.Static,
        List<PageAction>? actions = null) => new(selector, null, pageType, actions);

    /// <summary>
    /// Paginate step: apply <paramref name="itemSelector"/> on every page
    /// reached by walking <paramref name="paginationSelector"/> (the
    /// next-page links). The named factory for the paginate intent-shape
    /// (ADR-0030).
    /// </summary>
    /// <param name="itemSelector">The item links to follow on each page;
    /// non-empty.</param>
    /// <param name="paginationSelector">The next-page links; non-empty.</param>
    /// <param name="pageType">How the reached pages load.</param>
    /// <param name="actions">Browser actions for the reached pages; allowed
    /// only with <see cref="PageType.Dynamic"/>.</param>
    /// <exception cref="ArgumentException">either selector is
    /// null/empty/whitespace, or <paramref name="actions"/> is non-empty with
    /// <see cref="PageType.Static"/>.</exception>
    public static LinkPathSelector Paginate(
        string itemSelector,
        string paginationSelector,
        PageType pageType = PageType.Static,
        List<PageAction>? actions = null)
    {
        // The Paginate factory carries the paginate intent: a pagination step
        // with no pagination selector is malformed — it would be a plain
        // Follow. The primary constructor deliberately allows a null
        // PaginationSelector (that IS the Follow shape, and the JSON codec
        // round-trips it), so the pagination selector's required-ness is
        // enforced here, at the intent-carrying factory.
        ArgumentException.ThrowIfNullOrWhiteSpace(paginationSelector);
        return new(itemSelector, paginationSelector, pageType, actions);
    }

    /// <summary>
    /// Site-sweep step (ADR-0081): from the current page, extract it
    /// <em>and</em> recursively follow its on-domain links matching
    /// <paramref name="selector"/>, the child Jobs retaining this selector so
    /// the whole site is swept. The named factory for the recursive
    /// intent-shape, the third sibling beside <see cref="Follow"/> and
    /// <see cref="Paginate"/>. The on-domain boundary (same host plus
    /// <c>www</c>, widened by <c>--include-subdomains</c>) and the page/depth
    /// caps are crawl-global (<see cref="WebReaper.Domain.ScraperConfig"/>),
    /// not part of this per-step intent.
    /// </summary>
    /// <param name="selector">The links to sweep; defaults to <c>a[href]</c>
    /// (every anchor). Restrict it (for example <c>a[href^='/blog/']</c>) to
    /// sweep one section. Non-empty when supplied.</param>
    /// <param name="pageType">How the reached pages load.</param>
    /// <param name="actions">Browser actions for the reached pages; allowed
    /// only with <see cref="PageType.Dynamic"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="selector"/> is
    /// empty/whitespace, or <paramref name="actions"/> is non-empty with
    /// <see cref="PageType.Static"/>.</exception>
    public static LinkPathSelector Sweep(
        string selector = "a[href]",
        PageType pageType = PageType.Static,
        List<PageAction>? actions = null) =>
        new(selector, null, pageType, actions) { Recursive = true };

    // ADR-0030: the grammar is enforced at the construction site. A property
    // initializer over a primary-ctor parameter is the idiomatic positional-
    // record validation point — it runs on every construction path (direct
    // new, the ConfigBuilder fluent methods, the JSON codec's ReadSelector),
    // so an invalid LinkPathSelector is unrepresentable rather than a late
    // failure at the Crawl step.
    private static string NonEmptySelector(string selector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selector);
        return selector;
    }

    private static string? NonEmptyPaginationSelector(string? paginationSelector)
    {
        if (paginationSelector is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(paginationSelector);
        return paginationSelector;
    }

    private static List<PageAction>? ActionsMatchTransport(
        List<PageAction>? pageActions, PageType pageType)
    {
        // Empty equals absent (ADR-0028's rule, reused): only a non-empty
        // list with the static transport is the silent feature-drop bug.
        if (pageActions is { Count: > 0 } && pageType == PageType.Static)
            throw new ArgumentException(
                "PageActions require PageType.Dynamic — the static HTTP transport " +
                "ignores them. Use FollowWithBrowser / PaginateWithBrowser, or set " +
                "PageType.Dynamic.",
                nameof(pageActions));

        return pageActions;
    }
}
