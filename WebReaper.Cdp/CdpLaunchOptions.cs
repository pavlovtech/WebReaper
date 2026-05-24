namespace WebReaper.Cdp;

/// <summary>
/// Options for the launch-and-connect overload of <c>WithCdpPageLoader</c>
/// (ADR-0052). The transport spawns Chromium with
/// <c>--remote-debugging-port=0</c>, waits for the port, then connects.
/// </summary>
public sealed class CdpLaunchOptions
{
    /// <summary>Absolute path to the Chromium-family executable to launch.
    /// When <c>null</c>, the launcher searches PATH and platform-conventional
    /// install locations for <c>google-chrome</c>, <c>chromium</c>,
    /// <c>chrome</c>, <c>microsoft-edge</c>, <c>msedge</c>.</summary>
    public string? ExecutablePath { get; set; }

    /// <summary>Run the browser headless. Default <c>true</c>.</summary>
    public bool Headless { get; set; } = true;

    /// <summary>Additional command-line flags passed to the browser binary
    /// (e.g. <c>--no-sandbox</c>, <c>--disable-dev-shm-usage</c>). The
    /// transport always adds <c>--remote-debugging-port=0</c>; do not
    /// duplicate it here.</summary>
    public IReadOnlyList<string> AdditionalArgs { get; set; } = [];

    /// <summary>Optional user-data-dir. When <c>null</c> the launcher
    /// allocates a temp dir and removes it on disposal.</summary>
    public string? UserDataDir { get; set; }

    /// <summary>How long to wait for the spawned process to publish its
    /// CDP endpoint before failing. Default 30 seconds.</summary>
    public TimeSpan StartupTimeout { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>Spec for <see cref="CdpLaunchHelpers.LaunchAsync"/>.</summary>
/// <param name="ExecutablePath">Browser binary path.</param>
/// <param name="Args">Command-line args (the launcher adds
/// <c>--remote-debugging-port=0</c> and <c>--user-data-dir=...</c> if not
/// already present).</param>
/// <param name="UserDataDir">Optional pre-allocated user-data-dir; when
/// <c>null</c> the launcher creates a temp dir.</param>
/// <param name="StartupTimeout">How long to wait for the port to publish.</param>
public sealed record CdpLaunchSpec(
    string ExecutablePath,
    IReadOnlyList<string> Args,
    string? UserDataDir = null,
    TimeSpan? StartupTimeout = null);

/// <summary>What <see cref="CdpLaunchHelpers.LaunchAsync"/> returns —
/// the resolved CDP WebSocket URL plus a teardown handle that kills the
/// process and removes any temp user-data-dir.</summary>
public sealed class LaunchedCdpEndpoint : IAsyncDisposable
{
    private readonly Func<ValueTask> _disposeAsync;

    /// <summary>The <c>ws://...</c> CDP WebSocket URL ready to pass to
    /// <c>WithCdpPageLoader(string)</c>.</summary>
    public string CdpUrl { get; }

    /// <summary>The OS process ID of the spawned browser.</summary>
    public int ProcessId { get; }

    internal LaunchedCdpEndpoint(string cdpUrl, int processId, Func<ValueTask> disposeAsync)
    {
        CdpUrl = cdpUrl;
        ProcessId = processId;
        _disposeAsync = disposeAsync;
    }

    /// <summary>Kill the browser process and clean up the temp user-data-dir.</summary>
    public ValueTask DisposeAsync() => _disposeAsync();
}
