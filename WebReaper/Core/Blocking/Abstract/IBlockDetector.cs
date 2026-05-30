using WebReaper.Core.Loaders.Abstract;

namespace WebReaper.Core.Blocking.Abstract;

/// <summary>
/// The core seam that classifies one loaded page as a bot-check challenge or
/// not (ADR-0083), purely from its <see cref="PageLoadResult"/> (status,
/// headers, body). "Am I being blocked?" is a core scraping concern, so the
/// default <c>BlockDetector</c> ships in core; swap it via
/// <c>ScraperEngineBuilder.WithBlockDetector(...)</c>. Detection is reporting,
/// not acting: the <see cref="BlockVerdict"/> is data the Crawl driver acts on
/// (later slices), never a thrown exception.
/// </summary>
public interface IBlockDetector
{
    /// <summary>Classify <paramref name="result"/>. Pure: the same input always
    /// produces the same verdict, and it never throws.</summary>
    BlockVerdict Detect(PageLoadResult result);
}
