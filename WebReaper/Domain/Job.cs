using System.Collections.Immutable;
using WebReaper.Domain.PageActions;
using WebReaper.Domain.Selectors;

namespace WebReaper.Domain;

/// <summary>
/// One unit of crawl work: a URL plus the remaining
/// <see cref="LinkPathSelector"/> chain. The chain length is the crawl state
/// machine (ADR-0001) — the Crawl step dequeues one selector per step, and an
/// empty chain means "this is a target page, parse it with the Schema". Child
/// Jobs carry the advanced (shortened) chain. Immutable; created by the engine
/// from the start URLs and by the Crawl step from discovered links.
/// </summary>
/// <param name="Url">The page this Job crawls.</param>
/// <param name="LinkPathSelectors">The remaining selector chain; empty ⇒ a
/// target page (parsed with the Schema, no further Jobs).</param>
/// <param name="ParentBacklinks">The ancestor URLs that led here, oldest
/// first — surfaced to the PostProcessor as <see cref="Metadata"/>.</param>
/// <param name="PageType">Load statically (HTTP) or dynamically (headless
/// browser).</param>
/// <param name="PageActions">Browser actions to run before scraping a dynamic
/// page; null for a static page.</param>
public record Job(
    string Url,
    ImmutableQueue<LinkPathSelector> LinkPathSelectors,
    ImmutableQueue<string> ParentBacklinks,
    PageType PageType = PageType.Static,
    List<PageAction>? PageActions = null);
