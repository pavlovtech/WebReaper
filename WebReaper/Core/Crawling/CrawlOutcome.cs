using System.Collections.Immutable;
using WebReaper.Domain;
using WebReaper.Sinks.Models;

namespace WebReaper.Core.Crawling;

/// <summary>
/// The result of a <see cref="Abstract.ICrawlStep"/>: exactly one of a target
/// page's <see cref="ParsedData"/>, a transit page's followed Jobs, a
/// paginated page's item + next-page Jobs, or a <b>Sweep page</b>'s
/// <see cref="ParsedData"/> together with its on-domain child Jobs (ADR-0081).
/// Never more than one arm; the arm is a total function of the Job's selector
/// chain.
///
/// Closed hierarchy — construct only via the factory members; the union is not
/// extensible without adding a genuinely new Page category. See
/// docs/adr/0001-crawl-outcome-closed-sum.md and
/// docs/adr/0081-site-sweep-whole-site-crawl.md.
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

    /// <summary>Sweep page (ADR-0081): the one arm that both extracts
    /// <em>and</em> follows. <see cref="Data"/> is this page's parsed record
    /// (emitted like a <see cref="Parsed"/> target), and <see cref="Next"/>
    /// are its on-domain child Jobs that <b>retain</b> the recursive sweep
    /// selector, so the traversal perpetuates until the Visited-link tracker
    /// frontier saturates or the page-cap cutoff trips. Empty <see cref="Next"/>
    /// when no on-domain link matched or the depth cap was reached.</summary>
    /// <param name="Data">The scraped record for this Sweep page.</param>
    /// <param name="Next">The on-domain child Jobs, each retaining the
    /// recursive sweep selector.</param>
    public sealed record Swept(
        ParsedData Data,
        ImmutableArray<Job> Next) : CrawlOutcome;

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

    /// <summary>The Sweep-page arm (ADR-0081): <paramref name="data"/> is this
    /// page's parsed record and <paramref name="next"/> its on-domain child
    /// Jobs (which retain the recursive sweep selector).</summary>
    public static CrawlOutcome Sweep(
        ParsedData data,
        ImmutableArray<Job> next) => new Swept(data, next);

    /// <summary>All candidate next Jobs in deterministic order:
    /// <see cref="Followed.Next"/>, or <see cref="Paginated.Items"/> then
    /// <see cref="Paginated.NextPages"/>, or <see cref="Swept.Next"/>, or empty
    /// for <see cref="Parsed"/>. Candidates only; visited-link filtering is
    /// the caller's job.</summary>
    public ImmutableArray<Job> NextJobs => this switch
    {
        Followed f => f.Next,
        Paginated p => p.Items.AddRange(p.NextPages),
        Swept s => s.Next,
        _ => ImmutableArray<Job>.Empty
    };
}
