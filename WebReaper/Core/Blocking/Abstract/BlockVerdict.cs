namespace WebReaper.Core.Blocking.Abstract;

/// <summary>
/// The result of a <see cref="IBlockDetector"/> pass over one
/// <see cref="WebReaper.Core.Loaders.Abstract.PageLoadResult"/> (ADR-0083): the
/// confidence tier plus a human-readable reason for the firing signal. Carried
/// on the Job report so the Crawl driver can act on it without reclassifying.
/// </summary>
/// <param name="Confidence">How strongly the page looks like a challenge;
/// <see cref="BlockConfidence.None"/> means not blocked.</param>
/// <param name="Reason">A one-line description of the firing signal, or
/// <c>null</c> when not blocked.</param>
public sealed record BlockVerdict(BlockConfidence Confidence, string? Reason)
{
    /// <summary>Whether the page is a bot-check challenge at all.</summary>
    public bool IsBlocked => Confidence != BlockConfidence.None;

    /// <summary>The not-blocked verdict.</summary>
    public static readonly BlockVerdict None = new(BlockConfidence.None, null);
}
