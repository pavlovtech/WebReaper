using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using WebReaper.Cdp;
using WebReaper.Core.Actions.Abstract;
using WebReaper.Core.Actions.Concrete;
using WebReaper.Domain.PageActions;

namespace WebReaper.Cdp.Tests;

/// <summary>
/// ADR-0052 (CDP transport) + ADR-0057 follow-up (extraction of
/// <see cref="CdpPageActionDispatcher"/> to make the table testable). Pins
/// the per-arm CDP-primitive contract: each <see cref="PageAction"/> arm
/// reduces to a specific CDP call (or a sequence of calls) with the
/// expected JSON shape. Mirrors <c>SemanticActDispatchTests</c>'s
/// shape for the Puppeteer-shared coordinator.
/// </summary>
public class CdpPageActionDispatchTests
{
    private const string SessionId = "session-1";

    [Fact]
    public async Task Click_runs_runtime_evaluate_with_querySelector_click()
    {
        var session = new FakeCdpSession();
        var coord = new SemanticActCoordinator(NullResolver.Instance, NullLogger.Instance);

        await CdpPageActionDispatcher.PerformAsync(
            session, SessionId, new PageAction.Click("button.signin"), coord, default);

        var call = Assert.Single(session.Calls);
        Assert.Equal("Runtime.evaluate", call.Method);
        Assert.Equal(SessionId, call.SessionId);
        var expr = call.Params!["expression"]!.GetValue<string>();
        Assert.Contains("document.querySelector(", expr);
        Assert.Contains("\"button.signin\"", expr);
        Assert.Contains(".click()", expr);
    }

    [Fact]
    public async Task ScrollToEnd_runs_runtime_evaluate_with_scrollTo_scrollHeight()
    {
        var session = new FakeCdpSession();
        var coord = new SemanticActCoordinator(NullResolver.Instance, NullLogger.Instance);

        await CdpPageActionDispatcher.PerformAsync(
            session, SessionId, new PageAction.ScrollToEnd(), coord, default);

        var call = Assert.Single(session.Calls);
        Assert.Equal("Runtime.evaluate", call.Method);
        Assert.Equal("window.scrollTo(0, document.body.scrollHeight)",
            call.Params!["expression"]!.GetValue<string>());
    }

    [Fact]
    public async Task EvaluateExpression_passes_the_expression_through()
    {
        var session = new FakeCdpSession();
        var coord = new SemanticActCoordinator(NullResolver.Instance, NullLogger.Instance);

        await CdpPageActionDispatcher.PerformAsync(
            session, SessionId,
            new PageAction.EvaluateExpression("document.title"), coord, default);

        var call = Assert.Single(session.Calls);
        Assert.Equal("document.title", call.Params!["expression"]!.GetValue<string>());
    }

    [Fact]
    public async Task Wait_does_not_call_CDP()
    {
        var session = new FakeCdpSession();
        var coord = new SemanticActCoordinator(NullResolver.Instance, NullLogger.Instance);

        await CdpPageActionDispatcher.PerformAsync(
            session, SessionId, new PageAction.Wait(5), coord, default);

        Assert.Empty(session.Calls);
    }

    [Fact]
    public async Task WaitForSelector_polls_runtime_evaluate_until_found()
    {
        var session = new FakeCdpSession();
        var calls = 0;
        session.OnSend("Runtime.evaluate", _ =>
        {
            calls++;
            // First two polls miss; the third hits — exercises the polling loop.
            var found = calls >= 3;
            return new JsonObject
            {
                ["result"] = new JsonObject { ["value"] = found }
            };
        });
        var coord = new SemanticActCoordinator(NullResolver.Instance, NullLogger.Instance);

        await CdpPageActionDispatcher.PerformAsync(
            session, SessionId, new PageAction.WaitForSelector(".loaded", 5000),
            coord, default);

        Assert.True(calls >= 3);
        Assert.All(session.Calls, c => Assert.Equal("Runtime.evaluate", c.Method));
    }

    [Fact]
    public async Task WaitForSelector_throws_on_timeout()
    {
        var session = new FakeCdpSession();
        session.OnSend("Runtime.evaluate", _ => new JsonObject
        {
            ["result"] = new JsonObject { ["value"] = false }
        });
        var coord = new SemanticActCoordinator(NullResolver.Instance, NullLogger.Instance);

        await Assert.ThrowsAsync<TimeoutException>(() =>
            CdpPageActionDispatcher.PerformAsync(
                session, SessionId, new PageAction.WaitForSelector(".never", 100),
                coord, default));
    }

    [Fact]
    public async Task WaitForNetworkIdle_calls_session_WaitForNetworkIdleAsync()
    {
        var session = new FakeCdpSession();
        var coord = new SemanticActCoordinator(NullResolver.Instance, NullLogger.Instance);

        // No in-flight requests on the simulator → the wait debounces and
        // returns immediately.
        await CdpPageActionDispatcher.PerformAsync(
            session, SessionId, new PageAction.WaitForNetworkIdle(),
            coord, default);

        var call = Assert.Single(session.Calls);
        Assert.Equal("WaitForNetworkIdle", call.Method);
        Assert.Equal(SessionId, call.SessionId);
    }

    [Fact]
    public async Task SemanticAct_resolves_and_dispatches_concrete_arm()
    {
        var session = new FakeCdpSession();
        var resolver = new RecordingResolver(_ => new PageAction.Click(".resolved"));
        var coord = new SemanticActCoordinator(resolver, NullLogger.Instance);

        await CdpPageActionDispatcher.PerformAsync(
            session, SessionId, new PageAction.SemanticAct("click sign in"),
            coord, default);

        Assert.Equal(1, resolver.CallCount);
        // SemanticAct → GetHtml (Runtime.evaluate for outerHTML) → Click (Runtime.evaluate).
        Assert.Equal(2, session.Calls.Count);
        Assert.All(session.Calls, c => Assert.Equal("Runtime.evaluate", c.Method));
        Assert.Contains(session.Calls, c =>
            c.Params!["expression"]!.GetValue<string>().Contains(".resolved"));
    }

    [Fact]
    public async Task Evaluate_throws_CdpException_on_exceptionDetails()
    {
        var session = new FakeCdpSession();
        session.OnSend("Runtime.evaluate", _ => new JsonObject
        {
            ["exceptionDetails"] = new JsonObject { ["text"] = "ReferenceError: foo is not defined" }
        });
        var coord = new SemanticActCoordinator(NullResolver.Instance, NullLogger.Instance);

        var ex = await Assert.ThrowsAsync<CdpException>(() =>
            CdpPageActionDispatcher.PerformAsync(
                session, SessionId, new PageAction.EvaluateExpression("foo()"),
                coord, default));
        Assert.Contains("ReferenceError", ex.Message);
    }

    // ADR-0078 Axis A: C# has no closed-hierarchy exhaustiveness — a
    // discard-less switch expression over PageAction warns CS8509 even when
    // every arm is handled, so it cannot be both clean today and a compile
    // error when an arm is added (verified). This is the CI substitute for the
    // CDP transport: dispatch every PageAction arm and assert none reaches the
    // switch's `default: throw ArgumentOutOfRangeException`. The reflection
    // check forces a sample to be added when a PageAction arm is added, and the
    // dispatch then proves the transport actually handles it.
    [Fact]
    public async Task Every_PageAction_arm_dispatches_without_hitting_the_default()
    {
        PageAction[] samples =
        [
            new PageAction.Click(".x"),
            new PageAction.Wait(1),
            new PageAction.WaitForSelector(".x", 1000),
            new PageAction.WaitForNetworkIdle(),
            new PageAction.ScrollToEnd(),
            new PageAction.ScrollIntoView(".x"),
            new PageAction.EvaluateExpression("1"),
            new PageAction.Press("Enter"),
            new PageAction.Fill(".x", "y"),
            new PageAction.SemanticAct("do it"),
        ];

        // Completeness: the samples cover every concrete PageAction arm, so a
        // newly-added arm trips this until a sample is added here.
        var armTypes = typeof(PageAction).GetNestedTypes()
            .Where(t => t.IsSealed && t.IsSubclassOf(typeof(PageAction)))
            .ToHashSet();
        var sampled = samples.Select(s => s.GetType()).ToHashSet();
        Assert.True(armTypes.SetEquals(sampled),
            "Sample set must cover every PageAction arm. Add a sample for the new arm. " +
            $"Uncovered: [{string.Join(", ", armTypes.Except(sampled).Select(t => t.Name))}]");

        foreach (var arm in samples)
        {
            var session = new FakeCdpSession();
            // Every querySelector poll finds its element; every evaluate succeeds.
            session.OnSend("Runtime.evaluate", _ => new JsonObject
            {
                ["result"] = new JsonObject { ["value"] = true }
            });
            // SemanticAct resolves to a concrete arm via the resolver.
            var coord = new SemanticActCoordinator(
                new RecordingResolver(_ => new PageAction.Click(".resolved")),
                NullLogger.Instance);

            var ex = await Record.ExceptionAsync(() =>
                CdpPageActionDispatcher.PerformAsync(session, SessionId, arm, coord, default));

            Assert.False(ex is ArgumentOutOfRangeException,
                $"{arm.GetType().Name} fell through to the dispatcher's default (unhandled arm)");
            Assert.Null(ex);
        }
    }

    private sealed class RecordingResolver : IActionResolver
    {
        private readonly Func<string, PageAction?> _resolve;
        public int CallCount { get; private set; }
        public RecordingResolver(Func<string, PageAction?> resolve) => _resolve = resolve;
        public Task<PageAction?> ResolveAsync(
            string intent, string pageHtml, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(_resolve(intent));
        }
    }

    // Local stand-in for the core's internal NullActionResolver — the
    // CDP-tests assembly doesn't have InternalsVisibleTo on WebReaper.dll.
    // Functionally identical for the dispatcher's purposes: returns null.
    private sealed class NullResolver : IActionResolver
    {
        public static readonly NullResolver Instance = new();
        public Task<PageAction?> ResolveAsync(
            string intent, string pageHtml, CancellationToken cancellationToken = default)
            => Task.FromResult<PageAction?>(null);
    }
}
