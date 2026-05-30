namespace WebReaper.Core.Blocking.Abstract;

/// <summary>
/// How strongly a <see cref="IBlockDetector"/> believes a page is a bot-check
/// challenge (ADR-0083). A tier, not just a bool, because later slices branch on
/// it: a high-confidence block drops the page outright, a weak one drops only
/// when extraction also produced nothing, and only a high-confidence block lifts
/// a host's escalation floor.
/// </summary>
public enum BlockConfidence
{
    /// <summary>Not a block.</summary>
    None,

    /// <summary>A body-marker-only signal: a challenge string appeared in the
    /// page HTML. Weak because such strings can also appear in legitimate
    /// content.</summary>
    Weak,

    /// <summary>A challenge-class HTTP status or a challenge-signalling response
    /// header. High because a normal page does not carry these.</summary>
    High,
}
