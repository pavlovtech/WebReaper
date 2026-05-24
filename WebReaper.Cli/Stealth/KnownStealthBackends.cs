namespace WebReaper.Cli.Stealth;

/// <summary>
/// ADR-0055: AOT-friendly static registry of stealth backends the CLI
/// surface knows about. Library satellites (<c>WebReaper.Stealth.X</c>)
/// ship freely for direct library use; CLI integration requires adding
/// a row here via PR. The CLI cannot reflectively discover installed
/// satellites — AOT-published single binary; NuGet has no post-install
/// hooks; a curated static list is the working answer.
/// </summary>
public static class KnownStealthBackends
{
    /// <summary>Every backend the CLI's <c>webreaper stealth install</c>
    /// command can offer. Add new backends via PR.</summary>
    public static readonly StealthBackend[] All =
    [
        new StealthBackend(
            Name: "cloakbrowser",
            DisplayName: "CloakBrowser",
            RecommendedVersion: "0.3.30",
            SizeMb: 220,
            Description: "58 fingerprint patches; recommended",
            LicenseUrl: "https://github.com/CloakHQ/CloakBrowser/blob/main/BINARY-LICENSE.md",
            ReleaseUrlPattern: "https://github.com/CloakHQ/CloakBrowser/releases/download/v{version}/cloakbrowser-{rid}.tar.gz",
            BinaryName: "cloakbrowser",
            LaunchArgs:
            [
                "--no-first-run",
                "--no-default-browser-check",
                "--disable-background-networking",
                "--disable-background-timer-throttling",
                "--disable-renderer-backgrounding",
                "--disable-features=TranslateUI,Translate",
                "--disable-dev-shm-usage",
            ]),
        // Future entries — Patchright, Camoufox, undetected-chromedriver —
        // land here as community PRs alongside their library satellites.
    ];

    /// <summary>Look up a backend by name (case-insensitive); returns
    /// <c>null</c> if not registered.</summary>
    public static StealthBackend? Find(string name) =>
        All.FirstOrDefault(b =>
            string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>One entry in the curated <see cref="KnownStealthBackends"/>
/// list — everything the CLI needs to install + launch a stealth fork
/// without reflectively loading the library satellite.</summary>
/// <param name="Name">Short canonical id (lowercase, no spaces) — what the
/// user types after <c>webreaper stealth install</c>.</param>
/// <param name="DisplayName">Human-friendly name shown in the picker UI.</param>
/// <param name="RecommendedVersion">Pinned version the CLI installs by
/// default. Override with <c>--version</c>.</param>
/// <param name="SizeMb">Approximate download size for the prompt.</param>
/// <param name="Description">One-line value-prop for the picker.</param>
/// <param name="LicenseUrl">Binary-license URL shown in the Y/n prompt.</param>
/// <param name="ReleaseUrlPattern">Format string with <c>{version}</c> and
/// <c>{rid}</c> placeholders the CLI substitutes to build the download URL.</param>
/// <param name="BinaryName">Executable name in the unpacked archive (no
/// platform suffix; the CLI adds <c>.exe</c> on Windows).</param>
/// <param name="LaunchArgs">Vendor-recommended command-line flags the CLI
/// passes to the launched binary (in addition to
/// <c>--remote-debugging-port=0</c> which is added automatically).</param>
public sealed record StealthBackend(
    string Name,
    string DisplayName,
    string RecommendedVersion,
    int SizeMb,
    string Description,
    string LicenseUrl,
    string ReleaseUrlPattern,
    string BinaryName,
    IReadOnlyList<string> LaunchArgs);
