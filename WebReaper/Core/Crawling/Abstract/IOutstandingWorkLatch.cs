namespace WebReaper.Core.Crawling.Abstract;

/// <summary>
/// The Outstanding-work latch (ADR-0022): a centralized unit-credit
/// termination detector (Huang/Mattern, integer-count form). Seed one unit of
/// credit per start Job; before a Job's own unit is returned, credit its
/// discovered children — the credit-conservation ordering, so the counter can
/// never hit zero prematurely; when outstanding credit reaches zero the Crawl
/// has terminated and the latch trips <b>exactly once</b>.
///
/// Two adapters: the in-memory <c>Interlocked</c> counter (in-process Crawl
/// driver) and a distributed-atomic counter (Redis, slice 3) for the
/// distributed driver. Correctness rides on the idempotency authority
/// (<c>IVisitedLinkTracker.TryAddVisitedLinkAsync</c>): the driver only
/// credits / returns genuinely-new work, so at-least-once redelivery cannot
/// unbalance the count, and the trip is CAS-fenced so completion fires once.
/// </summary>
public interface IOutstandingWorkLatch
{
    /// <summary>Seed one unit of credit per initial start Job. Call once,
    /// before any work is processed.</summary>
    Task SeedAsync(int startJobCount);

    /// <summary>Credit <paramref name="childCount"/> freshly-discovered child
    /// units. MUST be called before <see cref="SignalProcessedAsync"/> for the
    /// parent that discovered them (credit conservation).</summary>
    Task AddAsync(int childCount);

    /// <summary>Return the just-processed Job's one unit of credit. Returns
    /// <c>true</c> to exactly one caller — the one whose return drove
    /// outstanding credit to zero — which then runs the one-shot end-of-crawl
    /// action. A distributed adapter CAS-fences the trip so an at-least-once
    /// redelivered zero cannot fire completion twice.</summary>
    Task<bool> SignalProcessedAsync();
}
