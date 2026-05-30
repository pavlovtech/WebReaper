using System.Collections.Concurrent;
using WebReaper.Core.Blocking.Abstract;

namespace WebReaper.Core.Loaders.Concrete;

/// <summary>
/// The <c>EscalatingPageLoader</c>'s per-run, per-host starting-tier memory
/// (ADR-0083 part 5). Bot protection is near-always site-wide, so once one page
/// on a host is confirmed blocked at a tier, every later same-host page should
/// start higher rather than re-pay the failed lower tier. This holds that floor.
/// <para>
/// The floor lifts <b>only on a high-confidence block</b> (a challenge-class
/// status or header), never on a weak body-marker block — so one false-positive
/// page (a post that merely names a vendor) cannot promote a whole legitimate
/// host. The floor never lowers. State lives for the loader's (engine's)
/// lifetime and resets with a fresh engine; the precedent is the ADR-0050
/// <c>SemanticActCoordinator</c> per-crawl cache.
/// </para>
/// <para>Thread-safe: a parallel crawl loads many same-host pages at once.</para>
/// </summary>
internal sealed class HostTierFloor
{
    // Host comparison is case-insensitive (hosts are). The value is the lowest
    // tier index a page on that host should start at.
    private readonly ConcurrentDictionary<string, int> _floors =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The starting tier index for <paramref name="host"/>; 0 (the
    /// lowest rung) until a high-confidence block lifts it.</summary>
    public int FloorFor(string host) => _floors.TryGetValue(host, out var tier) ? tier : 0;

    /// <summary>
    /// Raise <paramref name="host"/>'s floor to <paramref name="tier"/>, but
    /// only when <paramref name="confidence"/> is
    /// <see cref="BlockConfidence.High"/> — a weak body-marker block is too
    /// unreliable to promote a whole host. The floor never lowers: a lift to a
    /// tier at or below the current floor is a no-op.
    /// </summary>
    /// <param name="host">The host whose floor to raise.</param>
    /// <param name="tier">The tier index to raise the floor to (typically the
    /// rung above the one that blocked).</param>
    /// <param name="confidence">The blocking verdict's confidence; only
    /// <see cref="BlockConfidence.High"/> lifts.</param>
    public void Lift(string host, int tier, BlockConfidence confidence)
    {
        if (confidence != BlockConfidence.High) return;
        _floors.AddOrUpdate(host, tier, (_, current) => Math.Max(current, tier));
    }
}
