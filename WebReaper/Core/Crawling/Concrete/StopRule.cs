using Microsoft.Extensions.Logging;
using WebReaper.Core.Crawling.Abstract;
using WebReaper.Core.LinkTracker.Abstract;

namespace WebReaper.Core.Crawling.Concrete;

/// <summary>
/// The Crawl driver's stop rule (ADR-0032): the one home for "should this
/// Crawl stop, and why?". It composes the two termination conditions the
/// in-process driver previously checked inline:
/// <list type="bullet">
///   <item><b>Completion</b> — the Outstanding-work latch drained (every
///   discovered Job processed), active under <c>StopWhenDrained</c>.</item>
///   <item><b>Cutoff</b> — the soft page limit reached.</item>
/// </list>
/// The driver consults it instead of inlining latch calls and limit
/// arithmetic; the stop rule <em>reports</em> the verdict and the driver
/// <em>acts</em> on it (ceases its own consumption of the job stream —
/// ADR-0037) — the ADR-0001 / ADR-0022 posture. Once concluded it stays
/// concluded, and the conclusion is CAS-fenced so exactly one caller is told
/// to act. Built per-run inside <see cref="ScraperEngine.RunAsync"/> from the
/// config it needs.
/// </summary>
internal sealed class StopRule
{
    private readonly IOutstandingWorkLatch _latch;
    private readonly IVisitedLinkTracker _linkTracker;
    private readonly int _pageCrawlLimit;
    private readonly bool _stopWhenDrained;
    private readonly ILogger _logger;

    private int _concluded;
    private string? _stopReason;

    public StopRule(
        IOutstandingWorkLatch latch,
        IVisitedLinkTracker linkTracker,
        int pageCrawlLimit,
        bool stopWhenDrained,
        ILogger logger)
    {
        _latch = latch;
        _linkTracker = linkTracker;
        _pageCrawlLimit = pageCrawlLimit;
        _stopWhenDrained = stopWhenDrained;
        _logger = logger;
    }

    /// <summary>
    /// Has the Crawl already concluded? The driver's pre-crawl gate — a
    /// lock-free flag read, no per-Job visited-count round-trip.
    /// </summary>
    public bool IsCrawlOver => Volatile.Read(ref _concluded) == 1;

    /// <summary>The human-readable reason the Crawl concluded — set on the
    /// CAS-winning <see cref="Conclude"/> call; null until then. Consumed
    /// by the Crawl driver's ADR-0018 <c>CrawlStopped</c> trace event.</summary>
    public string? StopReason => Volatile.Read(ref _stopReason);

    /// <summary>
    /// Seed the Outstanding-work latch (under <c>StopWhenDrained</c>) and
    /// detect a Crawl that is over before the first crawl — no start work to
    /// drain, or a (resumed) visited count already at the page limit. Call
    /// once, before the job loop.
    /// </summary>
    public async Task SeedAsync(int startJobCount)
    {
        if (_stopWhenDrained)
            await _latch.SeedAsync(startJobCount);

        if (_stopWhenDrained && startJobCount == 0)
            Conclude(drained: true);
        else if (await LimitReachedAsync())
            Conclude(drained: false);
    }

    /// <summary>
    /// Register one processed Job (it discovered <paramref name="childCount"/>
    /// children): return its latch credit and credit its children in one
    /// atomic step (credit conservation is structural — ADR-0032), then check
    /// the page limit. Returns <c>true</c> to exactly one caller — the Job
    /// whose registration concluded the Crawl — which the driver answers by
    /// cancelling its own consumption of the job stream (ADR-0037).
    /// </summary>
    public async Task<bool> RegisterProcessedAsync(int childCount)
    {
        var drained = _stopWhenDrained && await _latch.SignalProcessedAsync(childCount);

        return drained
            ? Conclude(drained: true)
            : await LimitReachedAsync() && Conclude(drained: false);
    }

    private async Task<bool> LimitReachedAsync()
        => _pageCrawlLimit != int.MaxValue
           && await _linkTracker.GetVisitedLinksCount() >= _pageCrawlLimit;

    private bool Conclude(bool drained)
    {
        if (Interlocked.CompareExchange(ref _concluded, 1, 0) != 0)
            return false;

        var reason = drained
            ? "all outstanding work drained"
            : $"page crawl limit {_pageCrawlLimit} reached";
        Volatile.Write(ref _stopReason, reason);

        if (drained)
            _logger.LogInformation("Crawl complete: all outstanding work drained");
        else
            _logger.LogInformation("Page crawl limit {Limit} reached; stopping", _pageCrawlLimit);

        return true;
    }
}
