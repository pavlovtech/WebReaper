using WebReaper.Cli.Commands;

namespace WebReaper.Cli.Tests;

/// <summary>
/// ADR-0055. BrowserCommand's pure helpers: the OS-aware cache-path,
/// the Playwright-CDN RID resolver, and the expected-binary path layout
/// (mirrors what Playwright's CDN zip contains under each RID).
/// </summary>
public class BrowserCommandTests
{
    [Fact]
    public void GetWebReaperHome_returns_a_non_empty_path()
    {
        var home = BrowserCommand.GetWebReaperHome();
        Assert.False(string.IsNullOrWhiteSpace(home));
        // The path is OS-dependent (~/.webreaper on Unix; %LOCALAPPDATA%\WebReaper
        // on Windows). It must end with WebReaper or .webreaper.
        var name = Path.GetFileName(home);
        Assert.True(name is ".webreaper" or "WebReaper",
            $"Unexpected home dir name: {name}");
    }

    [Fact]
    public void GetPlaywrightRid_returns_a_known_rid()
    {
        var rid = BrowserCommand.GetPlaywrightRid();
        var validRids = new[] { "win64", "mac", "mac-arm64", "linux", "linux-arm64" };
        Assert.Contains(rid, validRids);
    }

    [Fact]
    public void ExpectedChromiumBinary_returns_a_path_under_cache_dir()
    {
        var cacheDir = "/tmp/test-cache";
        var path = BrowserCommand.ExpectedChromiumBinary(cacheDir);
        Assert.StartsWith(cacheDir, path);
        // The Playwright zip unpacks to chrome-<rid>/chrome (or chrome.exe).
        Assert.Contains("chrome", path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExpectedChromiumBinary_picks_OS_specific_executable_name()
    {
        var path = BrowserCommand.ExpectedChromiumBinary("/tmp/test");
        if (OperatingSystem.IsWindows())
        {
            Assert.EndsWith("chrome.exe", path);
        }
        else if (OperatingSystem.IsMacOS())
        {
            // On macOS, the binary is inside a .app bundle.
            Assert.Contains(".app", path);
            Assert.EndsWith("Chromium", path);
        }
        else
        {
            Assert.EndsWith("chrome", path);
        }
    }
}
