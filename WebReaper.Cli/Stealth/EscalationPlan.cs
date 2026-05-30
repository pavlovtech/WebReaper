namespace WebReaper.Cli.Stealth;

/// <summary>Which rung of the escalating loader a scrape's start page enters at
/// (ADR-0083 slice 5). The climb continues upward from here automatically.</summary>
internal enum StartTier
{
    /// <summary>Start at the HTTP rung and auto-climb to the browser rung on a
    /// block. The no-transport-flag default.</summary>
    Http,

    /// <summary>Start at the vanilla-browser rung (<c>--browser</c>); climb to
    /// stealth on a block when the stealth rung is included.</summary>
    Browser,

    /// <summary>Start at the stealth rung (<c>--stealth</c>); the top rung, no
    /// climb above it.</summary>
    Stealth,
}

/// <summary>Whether the stealth rung is part of the ladder this run
/// (ADR-0083 slice 5), decided once at startup.</summary>
internal enum StealthInclusion
{
    /// <summary>Include the stealth rung (explicit <c>--stealth</c> /
    /// <c>--auto-stealth</c> / <c>WEBREAPER_AUTO_STEALTH</c>).</summary>
    Included,

    /// <summary>Do not include it (<c>--no-auto-stealth</c>, or a plain scrape
    /// that never opted in).</summary>
    Excluded,

    /// <summary>Undecided by flags — ask the user (interactive) or default to
    /// excluded (non-interactive).</summary>
    AskUser,
}

/// <summary>
/// The pure flag-to-escalation mapping for <c>webreaper scrape</c> (ADR-0083
/// slice 5). No IO, no global state, so the starting-tier and stealth-inclusion
/// rules are unit-testable without a real scrape. The interactive Y/n prompt and
/// the stealth install live in the command; this only decides.
/// </summary>
internal static class EscalationPlan
{
    /// <summary>The rung the start page enters at: <c>--stealth</c> → stealth,
    /// <c>--browser</c> (or <c>--browser-cdp-url</c>, folded into
    /// <paramref name="browser"/>) → browser, otherwise HTTP.</summary>
    public static StartTier ResolveStartTier(bool browser, bool stealth) =>
        stealth ? StartTier.Stealth
        : browser ? StartTier.Browser
        : StartTier.Http;

    /// <summary>
    /// Whether the stealth rung is included, decided at startup:
    /// <list type="bullet">
    ///   <item><c>--stealth</c> → Included (explicit).</item>
    ///   <item><c>--no-auto-stealth</c> → Excluded (caps the climb at browser).</item>
    ///   <item><c>--auto-stealth</c> / env → Included (unattended yes).</item>
    ///   <item><c>--browser</c> with no stealth flag → AskUser (offer it once).</item>
    ///   <item>a plain scrape → Excluded (no speculative 220 MB download; the
    ///         residual-block hint points at <c>--stealth</c>).</item>
    /// </list>
    /// <c>--stealth</c> is checked first so it always includes the rung, even
    /// alongside a contradictory <c>--no-auto-stealth</c>.
    /// </summary>
    public static StealthInclusion ResolveStealth(
        bool browser, bool stealth, bool autoStealth, bool noAutoStealth) =>
        stealth ? StealthInclusion.Included
        : noAutoStealth ? StealthInclusion.Excluded
        : autoStealth ? StealthInclusion.Included
        : browser ? StealthInclusion.AskUser
        : StealthInclusion.Excluded;
}
