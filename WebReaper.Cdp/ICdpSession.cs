using System.Text.Json.Nodes;

namespace WebReaper.Cdp;

/// <summary>
/// The minimal CDP surface the per-arm <see cref="CdpPageActionDispatcher"/>
/// depends on — extracted so the dispatch table is unit-testable against a
/// fake without a real WebSocket. <see cref="CdpClient"/> is the production
/// implementation; <c>FakeCdpSession</c> in <c>WebReaper.Cdp.Tests</c> is
/// the test double.
/// </summary>
/// <remarks>
/// Internal: this is a test seam, not a public contract — the public
/// satellite surface is the transport + the builder extensions
/// (ADR-0052). The interface stays minimal; any new dispatcher need
/// that crosses the CDP boundary adds a method here and to the fake in
/// lockstep.
/// </remarks>
internal interface ICdpSession
{
    /// <summary>Send a CDP command and await its response. Mirrors
    /// <see cref="CdpClient.SendAsync"/>.</summary>
    Task<JsonNode> SendAsync(
        string method,
        JsonObject? parameters = null,
        string? sessionId = null,
        CancellationToken ct = default);

    /// <summary>Block until the per-session network-activity tracker reports
    /// zero in-flight requests for the debounce window, or the total timeout
    /// elapses (in which case the method returns normally — see ADR-0057
    /// §"Timeout semantics — log, don't throw"). Mirrors
    /// <see cref="CdpClient.WaitForNetworkIdleAsync"/>.</summary>
    Task WaitForNetworkIdleAsync(
        string sessionId,
        TimeSpan? debounce = null,
        TimeSpan? timeout = null,
        CancellationToken ct = default);
}
