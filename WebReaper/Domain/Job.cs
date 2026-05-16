using System.Collections.Immutable;
using WebReaper.Domain.PageActions;
using WebReaper.Domain.Selectors;

namespace WebReaper.Domain;

public record Job(
    string Url,
    ImmutableQueue<LinkPathSelector> LinkPathSelectors,
    ImmutableQueue<string> ParentBacklinks,
    PageType PageType = PageType.Static,
    List<PageAction>? PageActions = null);