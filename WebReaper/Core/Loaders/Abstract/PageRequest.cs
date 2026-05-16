using WebReaper.Domain.PageActions;
using WebReaper.Domain.Selectors;

namespace WebReaper.Core.Loaders.Abstract;

/// <summary>
/// What the page loader needs to fetch one page (ADR 0004): the URL, the
/// <see cref="Domain.Selectors.PageType"/> load mode, optional browser page
/// actions, and the headless flag. Projected from a <c>Job</c> plus the
/// crawl's headless setting — the loader never sees the selector chain or
/// backlinks. The HTTP transport ignores <see cref="PageActions"/> and
/// <see cref="Headless"/>; that is the accepted fat-field cost of having one
/// loading seam instead of two.
/// </summary>
public record PageRequest(
    string Url,
    PageType PageType,
    IReadOnlyList<PageAction>? PageActions = null,
    bool Headless = true);
