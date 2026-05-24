using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using WebReaper.Cdp;

namespace WebReaper.Cdp.Tests;

/// <summary>
/// Test double for <see cref="ICdpSession"/>. Records every
/// <see cref="SendAsync"/> call (method + params + sessionId) for assertion;
/// canned responses keyed by CDP method name; an optional in-memory network
/// activity simulator for <see cref="WaitForNetworkIdleAsync"/>.
/// </summary>
/// <remarks>
/// One <c>FakeCdpSession</c> per test. The fake is deliberately small —
/// the production responsibility under test is the dispatcher's JSON shape
/// + composition, not full CDP-protocol fidelity.
/// </remarks>
internal sealed class FakeCdpSession : ICdpSession
{
    private readonly Dictionary<string, Func<JsonObject?, JsonNode>> _responders = new();
    private readonly NetworkActivity _networkActivity = new();

    /// <summary>Calls recorded in order of dispatch.</summary>
    public List<RecordedCall> Calls { get; } = new();

    /// <summary>The simulator the dispatcher's WaitForNetworkIdle waits on.
    /// Tests advance it via <see cref="StartRequest"/> / <see cref="FinishRequest"/>.</summary>
    public NetworkActivity NetworkActivity => _networkActivity;

    /// <summary>Register a canned response for a CDP method. Tests typically
    /// only need one or two of these per scenario.</summary>
    public void OnSend(string method, Func<JsonObject?, JsonNode> respond)
        => _responders[method] = respond;

    public void OnSend(string method, JsonNode response)
        => _responders[method] = _ => response;

    public Task<JsonNode> SendAsync(
        string method, JsonObject? parameters = null, string? sessionId = null,
        CancellationToken ct = default)
    {
        Calls.Add(new RecordedCall(method, parameters, sessionId));
        if (_responders.TryGetValue(method, out var respond))
            return Task.FromResult(respond(parameters));
        // Default: empty result object — sufficient for most dispatch tests
        // (the dispatcher reads Runtime.evaluate's result.value field when it
        // matters; the test sets a responder for those cases explicitly).
        return Task.FromResult<JsonNode>(new JsonObject());
    }

    public Task WaitForNetworkIdleAsync(
        string sessionId, TimeSpan? debounce = null, TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        Calls.Add(new RecordedCall("WaitForNetworkIdle", null, sessionId));
        return _networkActivity.WaitForIdleAsync(
            debounce ?? TimeSpan.FromMilliseconds(500),
            timeout ?? TimeSpan.FromSeconds(30),
            ct);
    }

    public void StartRequest() => _networkActivity.OnRequestStarted();
    public void FinishRequest() => _networkActivity.OnRequestFinished();

    internal sealed record RecordedCall(string Method, JsonObject? Params, string? SessionId);
}
