namespace WebReaper.Core.Crawling.Abstract;

/// <summary>
/// The Outstanding-work latch (ADR-0022): a centralized unit-credit
/// termination detector (Huang/Mattern, integer-count form). Seed one unit of
/// credit per start Job; as each Job finishes, <see cref="SignalProcessedAsync"/>
/// returns that Job's unit and credits its discovered children in one atomic
/// step; when outstanding credit reaches zero the Crawl has terminated and the
/// latch trips <b>exactly once</b>.
///
/// Credit conservation is structural (ADR-0032): crediting the children and
/// returning the parent's unit are a single operation, so the counter can
/// never hit zero prematurely — there is no two-call ordering for a caller to
/// get wrong.
///
/// Two adapters: the in-memory <c>Interlocked</c> counter (in-process Crawl
/// driver) and a distributed-atomic counter (Redis) for the distributed
/// driver. Correctness rides on the idempotency authority
/// (<c>IVisitedLinkTracker.TryAddVisitedLinkAsync</c>): the driver only
/// signals genuinely-new work, so at-least-once redelivery cannot unbalance
/// the count, and the trip is CAS-fenced so completion fires once.
/// </summary>
public interface IOutstandingWorkLatch
{
    /// <summary>Seed one unit of credit per initial start Job. Call once,
    /// before any work is processed.</summary>
    Task SeedAsync(int startJobCount);

    /// <summary>
    /// Register one just-processed Job: credit its
    /// <paramref name="childCount"/> freshly-discovered children and return
    /// the Job's own unit, atomically (credit conservation — the net change to
    /// outstanding credit is <paramref name="childCount"/> minus one, so a Job
    /// that discovered nothing draws the count down and a Job that discovered
    /// more work than itself pushes it up). Returns <c>true</c> to exactly one
    /// caller — the one whose registration drove outstanding credit to zero —
    /// which then runs the one-shot end-of-crawl action. A distributed adapter
    /// CAS-fences the trip so an at-least-once redelivered zero cannot fire
    /// completion twice.
    /// </summary>
    Task<bool> SignalProcessedAsync(int childCount);
}
