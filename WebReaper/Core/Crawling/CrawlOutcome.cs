using System.Collections.Immutable;
using WebReaper.Domain;
using WebReaper.Sinks.Models;

namespace WebReaper.Core.Crawling;

/// <summary>
/// The result of a <see cref="Abstract.ICrawlStep"/>: exactly one of a target
/// page's <see cref="ParsedData"/>, a transit page's followed Jobs, or a
/// paginated page's item + next-page Jobs. Never more than one arm; the arm is
/// a total function of the Job's selector chain.
///
/// Closed hierarchy — construct only via the factory members; the union is not
/// extensible. See docs/adr/0001-crawl-outcome-closed-sum.md.
/// </summary>
public abstract record CrawlOutcome
{
    private CrawlOutcome() { }

    /// <summary>Target page (empty selector chain): the page was parsed with
    /// the Schema. No further Jobs.</summary>
    /// <param name="Data">The scraped record for the target page.</param>
    public sealed record Parsed(ParsedData Data) : CrawlOutcome;

    /// <summary>Transit page: the head selector was consumed. Every Job carries
    /// the <b>advanced</b> (shortened) selector chain. Empty when no links
    /// matched.</summary>
    /// <param name="Next">The follow Jobs, each carrying the advanced
    /// selector chain.</param>
    public sealed record Followed(ImmutableArray<Job> Next) : CrawlOutcome;

    /// <summary>Page with pagination. <see cref="Items"/> are item Jobs that
    /// <b>advance</b> — their chain is emptied, so they are target pages.
    /// <see cref="NextPages"/> are next-page Jobs that <b>retain</b> the same
    /// one-element paginated chain, because page 2 of a listing is the same
    /// step, not a deeper one. Either list may be empty.</summary>
    /// <param name="Items">Item Jobs whose chain is emptied (target pages).</param>
    /// <param name="NextPages">Next-page Jobs that retain the one-element
    /// paginated chain.</param>
    public sealed record Paginated(
        ImmutableArray<Job> Items,
        ImmutableArray<Job> NextPages) : CrawlOutcome;

    /// <summary>The target-page arm: the page was parsed into
    /// <paramref name="data"/>.</summary>
    public static CrawlOutcome Target(ParsedData data) => new Parsed(data);

    /// <summary>The transit arm: the head selector was consumed,
    /// <paramref name="next"/> are the follow Jobs.</summary>
    public static CrawlOutcome Transit(ImmutableArray<Job> next) => new Followed(next);

    /// <summary>The pagination arm: <paramref name="items"/> are the listing's
    /// item Jobs, <paramref name="nextPages"/> the next-page Jobs.</summary>
    public static CrawlOutcome Pagination(
        ImmutableArray<Job> items,
        ImmutableArray<Job> nextPages) => new Paginated(items, nextPages);

    /// <summary>All candidate next Jobs in deterministic order:
    /// <see cref="Followed.Next"/>, or <see cref="Paginated.Items"/> then
    /// <see cref="Paginated.NextPages"/>, or empty for <see cref="Parsed"/>.
    /// Candidates only — visited-link filtering is the caller's job.</summary>
    public ImmutableArray<Job> NextJobs => this switch
    {
        Followed f => f.Next,
        Paginated p => p.Items.AddRange(p.NextPages),
        _ => ImmutableArray<Job>.Empty
    };
}
