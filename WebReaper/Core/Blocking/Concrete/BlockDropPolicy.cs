using WebReaper.Core.Blocking.Abstract;

namespace WebReaper.Core.Blocking.Concrete;

/// <summary>
/// The pure confidence-split drop rule (ADR-0083 part 8): given a
/// <see cref="BlockVerdict"/> and the number of records the page extracted,
/// decide whether the Crawl driver should suppress the page (skip the
/// post-extraction pipeline and Sink fan-out) instead of emitting its content.
/// <para>
/// This is where record count re-enters the design. The
/// <see cref="IBlockDetector"/> is pure over the load stage and cannot see how
/// many records the page yielded, so a weak body-marker verdict cannot tell a
/// real page from a challenge on its own. The count is an extraction-stage fact;
/// the driver folds it in here, one layer up from the detector, never back inside
/// the seam. Detection reports, the driver acts.
/// </para>
/// </summary>
public static class BlockDropPolicy
{
    /// <summary>
    /// Decide whether to drop a page given its block verdict and extracted
    /// record count.
    /// <list type="bullet">
    ///   <item><see cref="BlockConfidence.High"/>: drop regardless of count. A
    ///   confirmed challenge (a challenge-class status or header) is not worth
    ///   emitting.</item>
    ///   <item><see cref="BlockConfidence.Weak"/>: drop only when
    ///   <paramref name="recordCount"/> is zero. A weak body-marker page that
    ///   still yielded real records was a false positive (or a beatable
    ///   challenge) and is kept, which is what stops a vendor-name false positive
    ///   from destroying a real page.</item>
    ///   <item><see cref="BlockConfidence.None"/>: never drop.</item>
    /// </list>
    /// </summary>
    /// <param name="verdict">The block detector's verdict for the page.</param>
    /// <param name="recordCount">How many records the page's extraction produced
    /// (0 for an empty extraction).</param>
    /// <returns><c>true</c> to suppress the page; <c>false</c> to emit it.</returns>
    public static bool ShouldDrop(BlockVerdict verdict, int recordCount)
    {
        ArgumentNullException.ThrowIfNull(verdict);

        return verdict.Confidence switch
        {
            BlockConfidence.High => true,
            BlockConfidence.Weak => recordCount == 0,
            _ => false,
        };
    }
}
