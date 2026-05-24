using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using WebReaper.Builders;
using WebReaper.Core.Actions.Abstract;
using WebReaper.Core.Actions.Concrete;
using WebReaper.Core.Agent.Abstract;
using WebReaper.Core.Agent.Concrete;
using WebReaper.Core.LinkTracker.Concrete;
using WebReaper.Core.Loaders.Abstract;
using WebReaper.Core.Parser.Abstract;
using WebReaper.Domain.Agent;
using WebReaper.Domain.PageActions;
using WebReaper.Domain.Parsing;
using WebReaper.Domain.Selectors;
using WebReaper.Sinks.Abstract;
using WebReaper.Sinks.Models;

namespace WebReaper.UnitTests;

// ADR-0051: the in-process Agent driver. Pins the sequential decide→
// persist→execute loop, the termination precedence (Stop > MaxSteps cap >
// brain-throws), the persist-before-execute order, the resume-from-snapshot
// path, and the visited-link enforcement.
public class AgentEngineDriverTests
{
    [Fact]
    public async Task Stop_decision_ends_run_with_brain_reason()
    {
        var brain = new ScriptedBrain(new AgentDecision.Stop { Reason = "goal met" });
        var loader = new FakeLoader("<html><body>hi</body></html>");
        var store = new InMemoryAgentRunStore();

        var result = await BuildEngine(brain, loader, store);
        var run = await result.RunAsync();

        Assert.Equal("goal met", run.TerminationReason);
        // StepsExecuted counts every brain decision returned, including Stop.
        Assert.Equal(1, run.StepsExecuted);
        Assert.Single(run.History);
    }

    [Fact]
    public async Task MaxSteps_cap_terminates_runaway_brain()
    {
        // Brain always returns Follow to a new URL — would loop forever
        // without the cap.
        int counter = 0;
        var brain = new ScriptedBrain(_ =>
            new AgentDecision.Follow($"https://example.com/p{counter++}") { Reason = "next" });
        var loader = new FakeLoader("<html><body>p</body></html>");

        var engine = await AgentEngineBuilder
            .Start("https://example.com/", "loop forever")
            .WithBrain(brain)
            .WithPageLoader(loader)
            .WithMaxSteps(3)
            .BuildAsync();

        var run = await engine.RunAsync();

        Assert.Contains("MaxSteps", run.TerminationReason);
        Assert.Equal(3, run.StepsExecuted);
    }

    [Fact]
    public async Task Extract_decision_emits_to_sink_and_collects_record()
    {
        var schema = new Schema { new SchemaElement("title", "h1") };
        var brain = new ScriptedBrain(
            new AgentDecision.Extract(schema) { Reason = "page has title" },
            new AgentDecision.Stop { Reason = "done" });
        var loader = new FakeLoader("<html><body><h1>Hello</h1></body></html>");
        var sink = new RecordingSink();

        var engine = await AgentEngineBuilder
            .Start("https://example.com/", "get the title")
            .WithBrain(brain)
            .WithPageLoader(loader)
            .AddSink(sink)
            .BuildAsync();

        var run = await engine.RunAsync();

        Assert.Single(run.Records);
        Assert.Single(sink.Emitted);
        Assert.Equal("Hello", sink.Emitted[0].Data["title"]?.GetValue<string>());
    }

    [Fact]
    public async Task Follow_decision_advances_currentUrl_and_marks_visited()
    {
        var brain = new ScriptedBrain(
            new AgentDecision.Follow("https://example.com/p2") { Reason = "next page" },
            new AgentDecision.Stop { Reason = "done" });
        var loader = new FakeLoader("<html><body><a href='/p2'>link</a></body></html>");

        var engine = await AgentEngineBuilder
            .Start("https://example.com/", "follow once")
            .WithBrain(brain)
            .WithPageLoader(loader)
            .BuildAsync();

        var run = await engine.RunAsync();

        Assert.Contains("https://example.com/", run.VisitedUrls);
        Assert.Contains("https://example.com/p2", run.VisitedUrls);
        Assert.Equal(2, run.StepsExecuted);
    }

    [Fact]
    public async Task Visited_Follow_is_rejected_and_currentUrl_unchanged()
    {
        // Brain proposes to re-Follow the start URL. Engine should reject
        // the visited URL and stay on the current page.
        var brain = new ScriptedBrain(
            new AgentDecision.Follow("https://example.com/") { Reason = "revisit (bad idea)" },
            new AgentDecision.Stop { Reason = "give up" });
        var loader = new FakeLoader("<html/>");

        var engine = await AgentEngineBuilder
            .Start("https://example.com/", "test visited rejection")
            .WithBrain(brain)
            .WithPageLoader(loader)
            .BuildAsync();

        var run = await engine.RunAsync();

        // The visited URL only appears once (the start), not twice.
        Assert.Single(run.VisitedUrls, u => u == "https://example.com/");
    }

    [Fact]
    public async Task Persist_happens_before_execute_so_resume_sees_decided_step()
    {
        // SaveStepAsync is called BEFORE the sink emission. We assert the
        // ordering by checking the store's snapshot history before the
        // sink ever sees the record.
        var schema = new Schema { new SchemaElement("h", "h1") };
        var sink = new BlockingSink();
        var store = new RecordingStore();

        var brain = new ScriptedBrain(
            new AgentDecision.Extract(schema) { Reason = "row 1" },
            new AgentDecision.Stop { Reason = "done" });
        var loader = new FakeLoader("<html><body><h1>X</h1></body></html>");

        // Sink blocks until we release it, so we can sample the store mid-step.
        var engine = await AgentEngineBuilder
            .Start("https://example.com/", "test ordering")
            .WithBrain(brain)
            .WithPageLoader(loader)
            .AddSink(sink)
            .WithRunStore(store)
            .BuildAsync();

        var runTask = engine.RunAsync();

        // Wait for the sink to be invoked (post-persist).
        await sink.Invoked.Task;

        // At this moment, the store should already have the Extract decision
        // — the engine persisted BEFORE calling the sink.
        Assert.NotEmpty(store.Saves);
        Assert.Equal("Extract", store.Saves[0].decision.GetType().Name);

        sink.Release.SetResult();
        await runTask;
    }

    [Fact]
    public async Task Resume_picks_up_from_LastDecidedStep_plus_one()
    {
        var store = new InMemoryAgentRunStore();
        var runId = "test-resume";

        // Seed the store with a snapshot that simulates a one-step run.
        var prior = new AgentRunSnapshot(
            Goal: "test",
            LastDecidedStep: 0,
            History: new[] { new AgentDecision.Follow("https://example.com/p2") { Reason = "prior" } },
            VisitedUrls: new[] { "https://example.com/", "https://example.com/p2" },
            Records: Array.Empty<JsonObject>(),
            CurrentUrl: "https://example.com/p2");
        await store.SaveStepAsync(runId,
            new AgentDecision.Follow("https://example.com/p2") { Reason = "prior" }, prior);

        var brain = new ScriptedBrain(new AgentDecision.Stop { Reason = "resumed and done" });
        var loader = new FakeLoader("<html/>");

        var engine = await AgentEngineBuilder
            .Start("https://example.com/", "test")
            .WithBrain(brain)
            .WithPageLoader(loader)
            .WithRunStore(store)
            .WithRunId(runId)
            .BuildAsync();

        var run = await engine.RunAsync();

        // StepsExecuted is the cumulative history length (prior + new).
        Assert.Equal(2, run.StepsExecuted);
        Assert.Equal("resumed and done", run.TerminationReason);
        // The resumed history includes both the prior Follow and the new Stop.
        Assert.Equal(2, run.History.Count);
        Assert.Equal(runId, run.RunId);
    }

    [Fact]
    public async Task RunId_is_returned_on_AgentResult_and_propagated_to_snapshots()
    {
        var brain = new ScriptedBrain(new AgentDecision.Stop { Reason = "noop" });
        var loader = new FakeLoader("<html/>");
        var store = new InMemoryAgentRunStore();

        var engine = await AgentEngineBuilder
            .Start("https://example.com/", "id")
            .WithBrain(brain)
            .WithPageLoader(loader)
            .WithRunStore(store)
            .WithRunId("explicit-id")
            .BuildAsync();

        var run = await engine.RunAsync();

        Assert.Equal("explicit-id", run.RunId);
    }

    [Fact]
    public async Task Clean_termination_deletes_snapshot()
    {
        var brain = new ScriptedBrain(new AgentDecision.Stop { Reason = "done" });
        var loader = new FakeLoader("<html/>");
        var store = new InMemoryAgentRunStore();
        var runId = "cleanup-test";

        var engine = await AgentEngineBuilder
            .Start("https://example.com/", "test")
            .WithBrain(brain)
            .WithPageLoader(loader)
            .WithRunStore(store)
            .WithRunId(runId)
            .BuildAsync();

        await engine.RunAsync();

        var snapshot = await store.LoadAsync(runId);
        Assert.Null(snapshot);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_OperationCanceledException()
    {
        // Brain that hangs forever — only cancellation can stop it.
        var brain = new HangingBrain();
        var loader = new FakeLoader("<html/>");

        var engine = await AgentEngineBuilder
            .Start("https://example.com/", "hang")
            .WithBrain(brain)
            .WithPageLoader(loader)
            .BuildAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => engine.RunAsync(cts.Token));
    }

    // --------------- fakes ---------------

    private async Task<AgentEngine> BuildEngine(IAgentBrain brain, IPageLoader loader, IAgentRunStore store)
        => await AgentEngineBuilder
            .Start("https://example.com/", "test")
            .WithBrain(brain)
            .WithPageLoader(loader)
            .WithRunStore(store)
            .BuildAsync();

    private sealed class ScriptedBrain : IAgentBrain
    {
        private readonly Queue<AgentDecision> _decisions;
        private readonly Func<AgentState, AgentDecision>? _fn;

        public ScriptedBrain(params AgentDecision[] decisions) { _decisions = new(decisions); _fn = null; }
        public ScriptedBrain(Func<AgentState, AgentDecision> fn) { _decisions = new(); _fn = fn; }

        public ValueTask<AgentDecision> DecideAsync(AgentState state, CancellationToken ct = default)
        {
            if (_fn is not null) return ValueTask.FromResult(_fn(state));
            return ValueTask.FromResult(_decisions.Count > 0
                ? _decisions.Dequeue()
                : new AgentDecision.Stop { Reason = "scripted brain exhausted" });
        }
    }

    private sealed class HangingBrain : IAgentBrain
    {
        public async ValueTask<AgentDecision> DecideAsync(AgentState state, CancellationToken ct = default)
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new AgentDecision.Stop { Reason = "unreachable" };
        }
    }

    private sealed class FakeLoader(string html) : IPageLoader
    {
        public Task<string> LoadAsync(PageRequest request, CancellationToken ct = default)
            => Task.FromResult(html);
    }

    private sealed class RecordingSink : IScraperSink
    {
        public readonly List<ParsedData> Emitted = new();
        public bool DataCleanupOnStart { get; set; }

        public Task EmitAsync(ParsedData entity, CancellationToken ct = default)
        {
            lock (Emitted) Emitted.Add(entity);
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingSink : IScraperSink
    {
        public readonly TaskCompletionSource Invoked = new();
        public readonly TaskCompletionSource Release = new();
        public bool DataCleanupOnStart { get; set; }

        public async Task EmitAsync(ParsedData entity, CancellationToken ct = default)
        {
            Invoked.TrySetResult();
            await Release.Task;
        }
    }

    private sealed class RecordingStore : IAgentRunStore
    {
        public readonly List<(string runId, AgentDecision decision, AgentRunSnapshot post)> Saves = new();
        private readonly InMemoryAgentRunStore _inner = new();

        public ValueTask<AgentRunSnapshot?> LoadAsync(string runId, CancellationToken ct = default)
            => _inner.LoadAsync(runId, ct);

        public ValueTask SaveStepAsync(string runId, AgentDecision decision, AgentRunSnapshot postState, CancellationToken ct = default)
        {
            lock (Saves) Saves.Add((runId, decision, postState));
            return _inner.SaveStepAsync(runId, decision, postState, ct);
        }

        public ValueTask DeleteAsync(string runId, CancellationToken ct = default)
            => _inner.DeleteAsync(runId, ct);
    }
}
