using WebReaper.Cli.Stealth;

namespace WebReaper.Cli.Tests;

/// <summary>
/// The empty-scrape hint advisor: a pure function over
/// <c>(browser, stealth, recordCount)</c>. It never escalates; it only
/// suggests the next transport to try when a scrape returns nothing.
/// </summary>
public class EmptyResultAdvisorTests
{
    // --- non-empty scrapes get no hint, regardless of transport ---

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Records_present_produces_no_hint(bool browser, bool stealth)
    {
        Assert.Null(EmptyResultAdvisor.Advise(browser, stealth, recordCount: 3));
    }

    // --- pure-HTTP empty scrape points at --browser (and mentions --stealth) ---

    [Fact]
    public void Http_empty_suggests_browser()
    {
        var hint = EmptyResultAdvisor.Advise(browser: false, stealth: false, recordCount: 0);
        Assert.NotNull(hint);
        Assert.Contains("--browser", hint);
        Assert.Contains("--stealth", hint);
    }

    // --- browser empty (no --stealth) points at --stealth ---

    [Fact]
    public void Browser_empty_suggests_stealth()
    {
        var hint = EmptyResultAdvisor.Advise(browser: true, stealth: false, recordCount: 0);
        Assert.NotNull(hint);
        Assert.Contains("--stealth", hint);
    }

    // --- --stealth empty does not loop the user back to --stealth ---

    [Fact]
    public void Stealth_empty_does_not_resuggest_stealth()
    {
        var hint = EmptyResultAdvisor.Advise(browser: true, stealth: true, recordCount: 0);
        Assert.NotNull(hint);
        Assert.DoesNotContain("--stealth", hint);
        // It should still say something useful (captcha / selectors), not a
        // generic blank line.
        Assert.NotEmpty(hint!);
    }
}
