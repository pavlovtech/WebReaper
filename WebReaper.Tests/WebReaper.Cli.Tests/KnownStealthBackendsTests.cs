using WebReaper.Cli.Stealth;

namespace WebReaper.Cli.Tests;

/// <summary>
/// ADR-0055. The curated static registry of stealth backends. CloakBrowser
/// is the v10.0.0 inaugural entry. Adding a backend = a row here +
/// (optionally) the matching library satellite; these tests pin the
/// shape so a missed field surfaces at PR-time.
/// </summary>
public class KnownStealthBackendsTests
{
    [Fact]
    public void Registry_has_at_least_one_backend()
    {
        Assert.NotEmpty(KnownStealthBackends.All);
    }

    [Fact]
    public void Cloakbrowser_is_registered()
    {
        var b = KnownStealthBackends.Find("cloakbrowser");
        Assert.NotNull(b);
        Assert.Equal("CloakBrowser", b!.DisplayName);
        Assert.Contains("CloakHQ", b.LicenseUrl);
        Assert.Contains("{version}", b.ReleaseUrlPattern);
        Assert.Contains("{rid}", b.ReleaseUrlPattern);
        Assert.NotEmpty(b.LaunchArgs);
    }

    [Fact]
    public void Find_is_case_insensitive()
    {
        Assert.NotNull(KnownStealthBackends.Find("CloakBrowser"));
        Assert.NotNull(KnownStealthBackends.Find("CLOAKBROWSER"));
        Assert.NotNull(KnownStealthBackends.Find("cloakbrowser"));
    }

    [Fact]
    public void Find_unknown_returns_null()
    {
        Assert.Null(KnownStealthBackends.Find("nonexistent"));
        Assert.Null(KnownStealthBackends.Find(""));
    }

    [Fact]
    public void Every_backend_has_required_metadata()
    {
        // Pins shape so a future PR adding a backend can't ship a partial row.
        foreach (var b in KnownStealthBackends.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(b.Name), $"Name missing: {b}");
            Assert.False(string.IsNullOrWhiteSpace(b.DisplayName), $"DisplayName missing: {b.Name}");
            Assert.False(string.IsNullOrWhiteSpace(b.RecommendedVersion), $"Version missing: {b.Name}");
            Assert.False(string.IsNullOrWhiteSpace(b.LicenseUrl), $"LicenseUrl missing: {b.Name}");
            Assert.False(string.IsNullOrWhiteSpace(b.BinaryName), $"BinaryName missing: {b.Name}");
            Assert.Contains("{version}", b.ReleaseUrlPattern);
            Assert.Contains("{rid}", b.ReleaseUrlPattern);
            Assert.True(b.SizeMb > 0, $"SizeMb is non-positive: {b.Name}");
        }
    }

    [Fact]
    public void Backend_names_are_lowercase_no_spaces()
    {
        // The Name is the CLI-token user types after `webreaper stealth install`.
        // Forbidding spaces and upper-case matches the ADR-0055 spec.
        foreach (var b in KnownStealthBackends.All)
        {
            Assert.Equal(b.Name, b.Name.ToLowerInvariant());
            Assert.DoesNotContain(' ', b.Name);
        }
    }
}
