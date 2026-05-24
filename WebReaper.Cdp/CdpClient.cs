using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;

// ADR-0052/0057: WebReaper.Cdp.Tests exercises CdpPageActionDispatcher
// and NetworkActivity against a FakeCdpSession (no real WebSocket).
[assembly: InternalsVisibleTo("WebReaper.Cdp.Tests")]

namespace WebReaper.Cdp;

/// <summary>
/// Minimal Chrome DevTools Protocol client (ADR-0052). Speaks the
/// flattened-session protocol over a single WebSocket: commands carry
/// optional <c>sessionId</c>, responses correlate by message <c>id</c>,
/// events are dispatched by <c>method</c> name. AOT-clean — no
/// reflection-driven serialisation; <see cref="JsonNode"/> throughout.
/// </summary>
/// <remarks>
/// Not a general-purpose CDP library. The transport
/// (<see cref="CdpPageLoadTransport"/>) is the one consumer; the client is
/// only as deep as that consumer needs. Sufficient for navigation,
/// JS evaluation, basic event waits, and content extraction — the seven
/// <c>PageAction</c> arms of ADR-0035.
/// </remarks>
public sealed class CdpClient : ICdpSession, IAsyncDisposable
{
    private readonly ClientWebSocket _ws;
    private readonly Uri _cdpUri;
    private readonly Channel<JsonObject> _eventQueue = Channel.CreateUnbounded<JsonObject>();
    private readonly Dictionary<int, TaskCompletionSource<JsonNode>> _pending = new();
    private readonly object _pendingLock = new();
    // ADR-0057: per-session network-activity trackers, fed by the read loop
    // on Network.* events; awaited by WaitForNetworkIdleAsync.
    private readonly ConcurrentDictionary<string, NetworkActivity> _networkTrackers = new();
    private readonly CancellationTokenSource _readLoopCts = new();
    private int _nextId;
    private Task? _readLoopTask;
    private bool _disposed;

    /// <summary>Construct against a CDP WebSocket URL
    /// (e.g. <c>ws://127.0.0.1:54321/devtools/browser/&lt;id&gt;</c>).
    /// Not yet connected — call <see cref="ConnectAsync"/>.</summary>
    public CdpClient(string cdpUrl)
    {
        if (string.IsNullOrWhiteSpace(cdpUrl))
            throw new ArgumentException("CDP URL is required.", nameof(cdpUrl));
        _cdpUri = new Uri(cdpUrl);
        _ws = new ClientWebSocket();
    }

    /// <summary>Open the WebSocket and start the read loop. Idempotent failure:
    /// throws on connect errors with the original exception preserved.</summary>
    public async Task ConnectAsync(CancellationToken ct)
    {
        await _ws.ConnectAsync(_cdpUri, ct);
        _readLoopTask = Task.Run(() => ReadLoopAsync(_readLoopCts.Token));
    }

    /// <summary>Send a CDP command and wait for its response. <paramref name="method"/>
    /// is a CDP method name (e.g. <c>"Page.navigate"</c>); <paramref name="parameters"/>
    /// is the <c>params</c> object; <paramref name="sessionId"/> targets a specific
    /// attached session (null for browser-level commands).</summary>
    public async Task<JsonNode> SendAsync(
        string method, JsonObject? parameters = null, string? sessionId = null,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonNode>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_pendingLock) _pending[id] = tcs;

        var message = new JsonObject
        {
            ["id"] = id,
            ["method"] = method,
        };
        if (parameters is not null) message["params"] = parameters;
        if (sessionId is not null) message["sessionId"] = sessionId;

        var bytes = Encoding.UTF8.GetBytes(message.ToJsonString());
        await _ws.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);

        // Respect external cancellation by cancelling the pending TCS.
        using var registration = ct.Register(() => tcs.TrySetCanceled(ct));
        return await tcs.Task;
    }

    /// <summary>Block until the per-session network-activity tracker reports
    /// zero in-flight requests for <paramref name="debounce"/> (default 500 ms),
    /// or <paramref name="timeout"/> (default 30 s) elapses. Returns normally
    /// on either outcome — the caller logs a warning if it wants to
    /// distinguish (ADR-0057 §"Timeout semantics — log, don't throw").
    /// </summary>
    /// <remarks>
    /// Lazily registers a <see cref="NetworkActivity"/> tracker on first call
    /// for a session; subsequent calls reuse it. The tracker is removed by
    /// <see cref="RemoveNetworkTracker"/> when the transport closes the
    /// session — keeps the dictionary bounded across long-lived browsers.
    /// </remarks>
    public Task WaitForNetworkIdleAsync(
        string sessionId,
        TimeSpan? debounce = null,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var tracker = _networkTrackers.GetOrAdd(sessionId, _ => new NetworkActivity());
        return tracker.WaitForIdleAsync(
            debounce ?? TimeSpan.FromMilliseconds(500),
            timeout ?? TimeSpan.FromSeconds(30),
            ct);
    }

    /// <summary>Drop the tracker for <paramref name="sessionId"/>. Called by
    /// the transport when it closes the per-navigation target — keeps the
    /// tracker map bounded across a long-lived browser WebSocket.</summary>
    internal void RemoveNetworkTracker(string sessionId)
    {
        _networkTrackers.TryRemove(sessionId, out _);
    }

    /// <summary>Wait for the next event matching <paramref name="method"/> and
    /// (optionally) <paramref name="sessionId"/>. Events emitted before the wait
    /// started are discarded — pre-arm before triggering them.</summary>
    public async Task<JsonObject> WaitForEventAsync(
        string method, string? sessionId = null,
        TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero) break;
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(remaining);
            try
            {
                var evt = await _eventQueue.Reader.ReadAsync(cts.Token);
                if (evt["method"]?.GetValue<string>() != method) continue;
                if (sessionId is not null && evt["sessionId"]?.GetValue<string>() != sessionId) continue;
                return evt;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                break; // local timeout
            }
        }
        throw new TimeoutException($"Timed out waiting for CDP event '{method}'.");
    }

    /// <summary>Async-disposable: close the WebSocket and cancel the read loop.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        try { _readLoopCts.Cancel(); } catch { }
        try
        {
            if (_ws.State == WebSocketState.Open)
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "shutdown", CancellationToken.None);
        }
        catch { /* socket already gone */ }
        _ws.Dispose();
        _readLoopCts.Dispose();
        // Fail any still-pending responses.
        lock (_pendingLock)
        {
            foreach (var tcs in _pending.Values) tcs.TrySetException(new ObjectDisposedException(nameof(CdpClient)));
            _pending.Clear();
        }
        if (_readLoopTask is not null)
        {
            try { await _readLoopTask; } catch { }
        }
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        var assembled = new MemoryStream();

        while (!ct.IsCancellationRequested && _ws.State == WebSocketState.Open)
        {
            assembled.SetLength(0);
            WebSocketReceiveResult result;
            do
            {
                try
                {
                    result = await _ws.ReceiveAsync(buffer, ct);
                }
                catch (OperationCanceledException) { return; }
                catch (WebSocketException) { return; }

                if (result.MessageType == WebSocketMessageType.Close) return;
                assembled.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            if (assembled.Length == 0) continue;

            JsonObject? parsed;
            try
            {
                parsed = JsonNode.Parse(assembled.ToArray()) as JsonObject;
            }
            catch
            {
                continue;
            }
            if (parsed is null) continue;

            if (parsed["id"] is JsonNode idNode)
            {
                var id = idNode.GetValue<int>();
                TaskCompletionSource<JsonNode>? tcs;
                lock (_pendingLock)
                {
                    if (_pending.TryGetValue(id, out tcs))
                        _pending.Remove(id);
                }
                if (tcs is null) continue;

                if (parsed["error"] is JsonObject err)
                {
                    var msg = err["message"]?.GetValue<string>() ?? "CDP error";
                    var code = err["code"]?.GetValue<int>() ?? 0;
                    tcs.TrySetException(new CdpException($"CDP error {code}: {msg}"));
                }
                else
                {
                    tcs.TrySetResult(parsed["result"] ?? new JsonObject());
                }
            }
            else
            {
                // ADR-0057: forward Network.* events to the per-session
                // activity tracker before pushing onto the event queue, so
                // WaitForNetworkIdleAsync waiters see request transitions
                // even if no other consumer is reading the queue.
                var method = parsed["method"]?.GetValue<string>();
                var sId = parsed["sessionId"]?.GetValue<string>();
                if (sId is not null && method is not null
                    && _networkTrackers.TryGetValue(sId, out var tracker))
                {
                    switch (method)
                    {
                        case "Network.requestWillBeSent":
                            tracker.OnRequestStarted();
                            break;
                        case "Network.loadingFinished":
                        case "Network.loadingFailed":
                        case "Network.requestServedFromCache":
                            tracker.OnRequestFinished();
                            break;
                    }
                }

                // Event — push onto the queue.
                await _eventQueue.Writer.WriteAsync(parsed, ct);
            }
        }
    }

    /// <summary>Test seam (ADR-0057): inject a network event from a
    /// fake/harness so the tracker advances without a real WebSocket.
    /// Internal — only callable from <c>WebReaper.Cdp.Tests</c>.</summary>
    internal void TestInjectNetworkEvent(string sessionId, string method)
    {
        if (!_networkTrackers.TryGetValue(sessionId, out var tracker)) return;
        switch (method)
        {
            case "Network.requestWillBeSent":
                tracker.OnRequestStarted();
                break;
            case "Network.loadingFinished":
            case "Network.loadingFailed":
            case "Network.requestServedFromCache":
                tracker.OnRequestFinished();
                break;
        }
    }
}

/// <summary>Raised when a CDP command returns an error response.</summary>
public sealed class CdpException : Exception
{
    /// <summary>Construct with a message.</summary>
    public CdpException(string message) : base(message) { }
}
