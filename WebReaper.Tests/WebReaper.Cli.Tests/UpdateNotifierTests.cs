using WebReaper.Cli;

namespace WebReaper.Cli.Tests;

// ADR-0082: the update notifier's pure logic: gating (TTY / CI / env),
// numeric version comparison, the 24h throttle, channel-aware upgrade hints,
// and message formatting. The network fetch + cache I/O + console write are
// thin glue tested at the wiring level; these pin the decisions.
public class UpdateNotifierTests
{
    // ---- gating (opt-out, TTY/CI/env) ----

    [Fact]
    public void Checks_when_interactive_and_unset()
    {
        Assert.True(UpdateNotifier.ShouldCheck(stderrIsTty: true, ciEnv: null, disableEnv: null, noNotifierEnv: null));
    }

    [Fact]
    public void Does_not_check_when_stderr_is_not_a_tty()
    {
        // Piped / redirected stderr (agents, CI logs); never notify.
        Assert.False(UpdateNotifier.ShouldCheck(stderrIsTty: false, ciEnv: null, disableEnv: null, noNotifierEnv: null));
    }

    [Theory]
    [InlineData("true")]
    [InlineData("1")]
    [InlineData("TRUE")]
    public void Does_not_check_under_ci(string ci)
    {
        Assert.False(UpdateNotifier.ShouldCheck(stderrIsTty: true, ciEnv: ci, disableEnv: null, noNotifierEnv: null));
    }

    [Theory]
    [InlineData("false")]
    [InlineData("0")]
    [InlineData("")]
    public void A_falsey_CI_value_does_not_count_as_ci(string ci)
    {
        Assert.True(UpdateNotifier.ShouldCheck(stderrIsTty: true, ciEnv: ci, disableEnv: null, noNotifierEnv: null));
    }

    [Fact]
    public void Does_not_check_when_disabled_by_either_env_var()
    {
        Assert.False(UpdateNotifier.ShouldCheck(true, null, disableEnv: "1", noNotifierEnv: null));
        Assert.False(UpdateNotifier.ShouldCheck(true, null, disableEnv: null, noNotifierEnv: "1"));
    }

    // ---- numeric semver comparison ----

    [Theory]
    [InlineData("v10.3.0", "10.2.0", true)]
    [InlineData("10.3.0", "10.2.0", true)]      // no leading v on the tag
    [InlineData("v10.2.1", "10.2.0", true)]
    [InlineData("v10.10.0", "10.9.0", true)]    // numeric, not lexical (10.10 > 10.9)
    [InlineData("v11.0.0", "10.9.9", true)]
    [InlineData("v10.2.0", "10.2.0", false)]
    [InlineData("v10.2.0", "10.2.0+abc123", false)]  // build metadata ignored
    [InlineData("v9.0.0", "10.0.0", false)]
    [InlineData("v10.2.0-rc1", "10.2.0", false)]     // a prerelease of the same version is not newer
    public void IsNewer_compares_numerically(string latest, string current, bool expected)
    {
        Assert.Equal(expected, UpdateNotifier.IsNewer(latest, current));
    }

    [Theory]
    [InlineData("v10.3.0", "dev")]              // unparseable current (dev build), never nag
    [InlineData("not-a-version", "10.2.0")]     // unparseable tag, swallow
    public void IsNewer_is_false_when_either_side_is_unparseable(string latest, string current)
    {
        Assert.False(UpdateNotifier.IsNewer(latest, current));
    }

    // ---- 24h throttle ----

    [Fact]
    public void Cache_is_stale_after_the_ttl()
    {
        var now = new DateTimeOffset(2026, 5, 30, 12, 0, 0, TimeSpan.Zero);
        Assert.True(UpdateNotifier.IsStale(now.AddHours(-25), now, TimeSpan.FromHours(24)));
        Assert.False(UpdateNotifier.IsStale(now.AddHours(-1), now, TimeSpan.FromHours(24)));
    }

    // ---- channel-aware upgrade hint ----

    [Theory]
    [InlineData("/opt/homebrew/bin/webreaper")]
    [InlineData("/usr/local/Cellar/webreaper/10.2.0/bin/webreaper")]
    [InlineData("/home/linuxbrew/.linuxbrew/bin/webreaper")]
    public void Homebrew_paths_hint_brew_upgrade(string path)
    {
        Assert.Contains("brew upgrade", UpdateNotifier.UpgradeHint(path));
    }

    [Fact]
    public void Winget_path_hints_winget_upgrade()
    {
        var path = @"C:\Users\x\AppData\Local\Microsoft\WinGet\Packages\pavlovtech.webreaper\webreaper.exe";
        Assert.Contains("winget upgrade", UpdateNotifier.UpgradeHint(path));
    }

    [Fact]
    public void Scoop_path_hints_scoop_update()
    {
        var path = @"C:\Users\x\scoop\apps\webreaper\current\webreaper.exe";
        Assert.Contains("scoop update", UpdateNotifier.UpgradeHint(path));
    }

    [Theory]
    [InlineData("/usr/local/bin/webreaper")]    // curl | sh install
    [InlineData(null)]                          // unknown
    public void Non_package_manager_paths_hint_the_install_script(string? path)
    {
        Assert.Contains("install.sh", UpdateNotifier.UpgradeHint(path));
    }

    // ---- message ----

    [Fact]
    public void Message_names_both_versions_the_hint_and_the_disable_var()
    {
        var msg = UpdateNotifier.FormatMessage("v10.3.0", "10.2.0+abc", "brew upgrade webreaper");
        Assert.Contains("10.3.0", msg);
        Assert.Contains("10.2.0", msg);
        Assert.DoesNotContain("abc", msg);          // build metadata stripped for display
        Assert.Contains("brew upgrade webreaper", msg);
        Assert.Contains("WEBREAPER_NO_UPDATE_CHECK", msg);
    }

    // ---- orchestration flow (EvaluateAsync, I/O injected) ----

    private static readonly DateTimeOffset Now = new(2026, 5, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Fresh_cache_with_a_newer_tag_notifies_without_fetching()
    {
        var fetched = false;
        var wrote = false;
        var msg = await UpdateNotifier.EvaluateAsync(
            currentVersion: "10.2.0",
            processPath: "/usr/local/bin/webreaper",
            now: Now,
            readCache: () => (Now.AddHours(-1), "v10.3.0"),   // fresh (within 24h)
            writeCache: _ => wrote = true,
            fetchLatest: () => { fetched = true; return Task.FromResult<string?>(null); });

        Assert.NotNull(msg);
        Assert.Contains("10.3.0", msg!);
        Assert.False(fetched);   // throttle short-circuits the network
        Assert.False(wrote);     // and the re-stamp
    }

    [Fact]
    public async Task Fresh_cache_at_the_current_version_says_nothing()
    {
        var msg = await UpdateNotifier.EvaluateAsync(
            "10.3.0", "/usr/local/bin/webreaper", Now,
            () => (Now.AddHours(-1), "v10.3.0"),
            _ => { }, () => Task.FromResult<string?>(null));

        Assert.Null(msg);
    }

    [Fact]
    public async Task Stale_cache_fetches_notifies_and_restamps()
    {
        (DateTimeOffset LastCheck, string? LatestTag)? written = null;
        var msg = await UpdateNotifier.EvaluateAsync(
            "10.2.0", "/opt/homebrew/bin/webreaper", Now,
            () => (Now.AddHours(-25), "v10.2.0"),             // stale, old tag
            e => written = e,
            () => Task.FromResult<string?>("v10.3.0"));        // fetch finds newer

        Assert.NotNull(msg);
        Assert.Contains("brew upgrade", msg!);                 // homebrew path → brew hint
        Assert.Equal((Now, "v10.3.0"), written);              // re-stamped with the new tag
    }

    [Fact]
    public async Task No_cache_fetches_on_first_run()
    {
        var fetched = false;
        var msg = await UpdateNotifier.EvaluateAsync(
            "10.2.0", null, Now,
            () => null,                                        // first run, no cache
            _ => { },
            () => { fetched = true; return Task.FromResult<string?>("v10.3.0"); });

        Assert.True(fetched);
        Assert.NotNull(msg);
    }

    [Fact]
    public async Task Stale_cache_with_a_failed_fetch_keeps_the_old_tag_and_restamps()
    {
        // Offline run: keep notifying from the cached tag, but re-stamp the
        // check time so we do not hammer the network every invocation.
        (DateTimeOffset LastCheck, string? LatestTag)? written = null;
        var msg = await UpdateNotifier.EvaluateAsync(
            "10.2.0", "/usr/local/bin/webreaper", Now,
            () => (Now.AddHours(-25), "v10.3.0"),             // stale, cached tag already newer
            e => written = e,
            () => Task.FromResult<string?>(null));            // fetch fails

        Assert.NotNull(msg);
        Assert.Equal((Now, "v10.3.0"), written);
    }

    [Fact]
    public async Task Dev_build_never_notifies_or_fetches()
    {
        var fetched = false;
        var msg = await UpdateNotifier.EvaluateAsync(
            "dev", null, Now,
            () => null, _ => { },
            () => { fetched = true; return Task.FromResult<string?>("v10.3.0"); });

        Assert.Null(msg);
        Assert.False(fetched);
    }
}
