namespace WebReaper.Cli.Stealth;

/// <summary>
/// A best-effort hint emitted when a <c>webreaper scrape</c> attempt returns
/// no records. Pure function over <c>(browser, stealth, recordCount)</c> with
/// no IO and no global state, so it is unit-testable without a real scrape.
/// </summary>
/// <remarks>
/// <para>
/// This is the <b>Empty result</b> hint (ADR-0083), distinct from a
/// <b>Blocked page</b>: the core block detector and the escalating loader handle
/// real blocks (climb, then suppress and exit non-zero), while this advisor only
/// points the user at the next transport to try when a scrape comes back empty.
/// It never escalates and never changes the exit code. It writes a single stderr
/// line so an empty result is not silently indistinguishable from "the page
/// genuinely had nothing".
/// </para>
/// <para>
/// Empty-but-fine is a real state (a <c>--schema</c> scrape whose selectors
/// legitimately match nothing), so the hint is phrased as a possibility, not a
/// verdict. The cost asymmetry favours the hint: one extra stderr line the
/// user ignores, versus an unexplained empty stdout.
/// </para>
/// <para>
/// The <paramref name="stealth"/> branch only avoids looping a <c>--stealth</c>
/// user back to the same flag: when stealth already ran (ADR-0083 slice 5 wires
/// <c>--stealth</c> to start the climb at the stealth rung) and still produced
/// nothing, pointing them at <c>--stealth</c> again is unhelpful, so the hint
/// names a captcha solver or a schema mismatch instead.
/// </para>
/// </remarks>
internal static class EmptyResultAdvisor
{
    /// <summary>Returns a one-line hint for an empty scrape, or <c>null</c>
    /// when no hint applies (records were produced).</summary>
    /// <param name="browser">Whether the attempt used a browser transport.</param>
    /// <param name="stealth">Whether <c>--stealth</c> was requested.</param>
    /// <param name="recordCount">How many records the scrape emitted.</param>
    public static string? Advise(bool browser, bool stealth, int recordCount)
    {
        if (recordCount > 0)
            return null;

        if (!browser)
            return "0 records extracted. The page may be JavaScript-rendered or "
                 + "blocking plain HTTP requests; retry with --browser "
                 + "(or --stealth for bot-protected sites).";

        if (stealth)
            // --stealth already requested; do not loop the user back to it.
            return "0 records extracted. The site may need a captcha solver, or "
                 + "the schema selectors may not match the page.";

        return "0 records extracted. If the site uses bot protection "
             + "(Cloudflare, DataDome, etc.), retry with --stealth.";
    }
}
