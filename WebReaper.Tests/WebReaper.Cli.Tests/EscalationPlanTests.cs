using WebReaper.Cli.Stealth;

namespace WebReaper.Cli.Tests;

/// <summary>
/// ADR-0083 slice 5: the pure flag-to-escalation mapping — where the climb
/// starts and whether the stealth rung is included. No IO, so it is a closed
/// truth table. (Facts rather than a Theory because the verdict enums are
/// internal — a public Theory parameter cannot be an internal type.)
/// </summary>
public class EscalationPlanTests
{
    // ----- ResolveStartTier: flags -> entry rung -----

    [Fact]
    public void Plain_scrape_starts_at_http() =>
        Assert.Equal(StartTier.Http, EscalationPlan.ResolveStartTier(browser: false, stealth: false));

    [Fact]
    public void Browser_flag_starts_at_the_browser_rung() =>
        Assert.Equal(StartTier.Browser, EscalationPlan.ResolveStartTier(browser: true, stealth: false));

    [Fact]
    public void Stealth_flag_starts_at_the_stealth_rung() =>
        Assert.Equal(StartTier.Stealth, EscalationPlan.ResolveStartTier(browser: true, stealth: true));

    [Fact]
    public void Stealth_wins_even_if_the_browser_bit_is_unset() =>
        Assert.Equal(StartTier.Stealth, EscalationPlan.ResolveStartTier(browser: false, stealth: true));

    // ----- ResolveStealth: startup inclusion policy -----

    [Fact]
    public void Plain_scrape_excludes_stealth_no_speculative_download() =>
        Assert.Equal(StealthInclusion.Excluded,
            EscalationPlan.ResolveStealth(browser: false, stealth: false, autoStealth: false, noAutoStealth: false));

    [Fact]
    public void Browser_with_no_stealth_flag_asks_the_user() =>
        Assert.Equal(StealthInclusion.AskUser,
            EscalationPlan.ResolveStealth(browser: true, stealth: false, autoStealth: false, noAutoStealth: false));

    [Fact]
    public void No_auto_stealth_caps_at_the_browser_rung() =>
        Assert.Equal(StealthInclusion.Excluded,
            EscalationPlan.ResolveStealth(browser: true, stealth: false, autoStealth: false, noAutoStealth: true));

    [Fact]
    public void Auto_stealth_includes_unattended() =>
        Assert.Equal(StealthInclusion.Included,
            EscalationPlan.ResolveStealth(browser: true, stealth: false, autoStealth: true, noAutoStealth: false));

    [Fact]
    public void Stealth_flag_includes_explicitly() =>
        Assert.Equal(StealthInclusion.Included,
            EscalationPlan.ResolveStealth(browser: true, stealth: true, autoStealth: false, noAutoStealth: false));

    [Fact]
    public void Stealth_flag_wins_over_no_auto_stealth() =>
        Assert.Equal(StealthInclusion.Included,
            EscalationPlan.ResolveStealth(browser: true, stealth: true, autoStealth: false, noAutoStealth: true));
}
