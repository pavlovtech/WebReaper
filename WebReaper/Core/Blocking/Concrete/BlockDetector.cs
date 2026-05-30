using WebReaper.Core.Blocking.Abstract;
using WebReaper.Core.Loaders.Abstract;

namespace WebReaper.Core.Blocking.Concrete;

/// <summary>
/// The default <see cref="IBlockDetector"/> (ADR-0083): a pure heuristic over a
/// <see cref="PageLoadResult"/>'s status, headers, and body. Ports the ADR-0056
/// CLI bot-check detector and adds the response-header signal the loader
/// widening unlocked. Record count is deliberately NOT an input (ADR-0083): it
/// is an extraction-stage fact the Crawl driver folds into its drop decision one
/// layer up, which is what keeps a weak body-marker false positive from
/// destroying a real page.
/// </summary>
/// <remarks>
/// Confidence tiers:
/// <list type="bullet">
///   <item><b>High</b>: a challenge-class HTTP status (403 / 429 / 503), or a
///   challenge-signalling response header. A normal page carries neither.</item>
///   <item><b>Weak</b>: a challenge-structural string in the body HTML. The
///   marker list is tightened from ADR-0056 to drop bare vendor names
///   ("DataDome", "Akamai") that appear in legitimate content, because under
///   page suppression and host-stickiness (later slices) a false positive is
///   costly.</item>
/// </list>
/// </remarks>
public sealed class BlockDetector : IBlockDetector
{
    /// <inheritdoc/>
    public BlockVerdict Detect(PageLoadResult result)
    {
        // High: a challenge-class HTTP status, independent of the body.
        if (result.HttpStatus is 403 or 429 or 503)
            return new BlockVerdict(BlockConfidence.High,
                $"HTTP {result.HttpStatus}: a challenge-class status.");

        // High: a challenge-signalling response header. Kept high-precision: a
        // mere "behind a WAF" header (e.g. cf-ray, which is present on ALL
        // Cloudflare traffic, blocked or not) is NOT a block signal and would
        // false-positive on every Cloudflare-fronted site, so it is excluded.
        foreach (var key in ChallengeHeaderKeys)
            if (result.Headers.ContainsKey(key))
                return new BlockVerdict(BlockConfidence.High,
                    $"Challenge-signalling response header: '{key}'.");

        // Weak: a challenge-structural marker in the body.
        if (!string.IsNullOrWhiteSpace(result.Html))
        {
            foreach (var marker in WeakBodyMarkers)
                if (result.Html.Contains(marker, StringComparison.OrdinalIgnoreCase))
                    return new BlockVerdict(BlockConfidence.Weak,
                        $"Challenge marker in body: '{marker}'.");
        }

        return BlockVerdict.None;
    }

    // High-confidence challenge response-header keys (looked up case-insensitively
    // via PageLoadResult.Headers). Curated and high-precision: only a header a
    // normal response does not carry. cf-mitigated is the header Cloudflare sets
    // when it is actively challenging (the 200-with-Turnstile case the status
    // signal misses). WAF-presence headers (cf-ray, server names) are excluded.
    internal static readonly string[] ChallengeHeaderKeys =
    [
        "cf-mitigated",
    ];

    // Weak-confidence body markers. Tightened from ADR-0056: bare vendor names
    // ("DataDome", "Akamai") are dropped because they appear in legitimate
    // content. Adding a marker is a one-line change plus a BlockDetectorTests row.
    internal static readonly string[] WeakBodyMarkers =
    [
        // Cloudflare Turnstile / managed challenge
        "Just a moment...",
        "Checking your browser",
        "cf-chl-bypass",
        // DataDome
        "dd-rd",
        // PerimeterX (HUMAN)
        "px-captcha",
        "_pxhd",
        // Imperva Incapsula
        "_Incapsula_",
        "Incapsula incident ID",
        // Akamai Bot Manager
        "ak_bmsc",
        "/akam/",
    ];
}
