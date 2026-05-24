namespace WebReaper.Cli.Stealth;

/// <summary>
/// ADR-0056. The conservative bot-check heuristic the CLI's
/// <c>webreaper scrape</c> Hybrid-C escalation path runs after the first
/// scrape attempt. Pure function over <c>(httpStatus, renderedHtml,
/// recordCount)</c> — no IO, no time, no global state — so it is unit-
/// testable without a real CDP browser.
/// </summary>
/// <remarks>
/// <para>
/// The detector is conservative by design: false positives are an extra
/// Y/n prompt the user dismisses; false negatives ship empty data the
/// user can't explain. Cost-asymmetric → err toward the prompt.
/// </para>
/// <para>
/// Two signals (OR-combined):
/// <list type="number">
///   <item>HTTP status in <c>{403, 429, 503}</c> — typical challenge-class
///         responses, regardless of body.</item>
///   <item>Zero records on a non-empty page <em>AND</em> the rendered HTML
///         contains a known challenge marker (Cloudflare / DataDome /
///         PerimeterX / Incapsula / Akamai).</item>
/// </list>
/// </para>
/// </remarks>
internal static class BotCheckDetector
{
    /// <summary>The result of a detection pass. <see cref="LikelyBlocked"/>
    /// drives the Y/n escalation prompt; <see cref="Reason"/> is the
    /// human-readable signal that fired.</summary>
    /// <param name="LikelyBlocked">True iff one of the two signals fired.</param>
    /// <param name="Reason">Null when <see cref="LikelyBlocked"/> is false;
    /// otherwise a one-line description of the firing signal.</param>
    public sealed record Verdict(bool LikelyBlocked, string? Reason)
    {
        public static readonly Verdict NoSignal = new(false, null);
    }

    /// <summary>Run the detector. Pure: same inputs always produce the
    /// same verdict.</summary>
    /// <param name="httpStatus">The HTTP status code of the main-document
    /// response, when available; <c>null</c> on the CDP-browser path
    /// (ADR-0056 §Accepted cost — the v10.x CDP transport doesn't yet
    /// surface this; only Signal 2 fires for <c>--browser</c> scrapes).</param>
    /// <param name="renderedHtml">The page document the
    /// <c>IContentExtractor</c> saw, or null if the scrape failed before
    /// loading.</param>
    /// <param name="recordCount">How many records the scrape emitted.
    /// Zero is the necessary (not sufficient) Signal-2 precondition.</param>
    public static Verdict Detect(int? httpStatus, string? renderedHtml, int recordCount)
    {
        // Signal 1 — challenge-class HTTP status. Independent of body.
        if (httpStatus is 403 or 429 or 503)
            return new(true, $"HTTP {httpStatus} — typical bot-check response code.");

        // Signal 2 — zero records on a non-empty challenge-marker page.
        // The conjunction (zero records AND non-empty AND marker) keeps the
        // false-positive rate bounded: legitimate empty pages don't match
        // (no markers); successful scrapes don't match (records > 0).
        if (recordCount == 0 && !string.IsNullOrWhiteSpace(renderedHtml))
        {
            foreach (var marker in ChallengeMarkers)
                if (renderedHtml.Contains(marker, StringComparison.OrdinalIgnoreCase))
                    return new(true,
                        $"Zero records on a page containing a challenge marker: '{marker}'.");
        }

        return Verdict.NoSignal;
    }

    /// <summary>The substring markers Signal-2 matches against the rendered
    /// HTML. Empirical (not exhaustive). Adding a marker is a one-line PR +
    /// a new <c>BotCheckDetectorTests</c> row.</summary>
    internal static readonly string[] ChallengeMarkers =
    [
        // Cloudflare Turnstile / managed challenge
        "Just a moment...",
        "Checking your browser",
        "cf-mitigated",
        "cf-chl-bypass",

        // DataDome
        "DataDome",
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
