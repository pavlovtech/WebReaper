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
using WebReaper.Infra.Abstract;
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

    // -------- ADR-0061: LastDecisionOutcome on AgentState --------

    [Fact]
    public async Task First_step_brain_sees_LastOutcome_None()
    {
        var observed = new List<AgentDecisionOutcome>();
        var brain = new CapturingBrain(
            captureTo: observed,
            scripted: new AgentDecision.Stop { Reason = "done" });

        var engine = await AgentEngineBuilder
            .Start("https://example.com/", "first-step outcome")
            .WithBrain(brain)
            .WithPageLoader(new FakeLoader("<html/>"))
            .BuildAsync();

        await engine.RunAsync();

        Assert.Single(observed);
        Assert.IsType<AgentDecisionOutcome.None>(observed[0]);
    }

    [Fact]
    public async Task After_successful_Extract_next_step_sees_Extracted_outcome()
    {
        var schema = new Schema { new SchemaElement("title", "h1") };
        var observed = new List<AgentDecisionOutcome>();
        var brain = new CapturingBrain(observed,
            new AgentDecision.Extract(schema) { Reason = "row 1" },
            new AgentDecision.Stop { Reason = "done" });

        var engine = await AgentEngineBuilder
            .Start("https://example.com/", "post-extract outcome")
            .WithBrain(brain)
            .WithPageLoader(new FakeLoader("<html><body><h1>Hello</h1></body></html>"))
            .BuildAsync();

        await engine.RunAsync();

        Assert.Equal(2, observed.Count);
        Assert.IsType<AgentDecisionOutcome.None>(observed[0]);
        var extracted = Assert.IsType<AgentDecisionOutcome.Extracted>(observed[1]);
        Assert.Equal(1, extracted.RecordCount);
        Assert.NotNull(extracted.Record);
        Assert.Equal("Hello", extracted.Record!["title"]?.GetValue<string>());
    }

    [Fact]
    public async Task Extract_returning_empty_object_surfaces_validation_Failed_outcome()
    {
        // ADR-0061 ↔ ADR-0062: the default SchemaSatisfiedValidator flags an
        // extraction missing required leaves as invalid; the engine surfaces
        // the verdict as Failed("validation: <reason>") on the next step so
        // the brain can revise. Here the schema declares a 'title' leaf; the
        // empty extractor returns {} (no title key); the validator reports
        // the missing required field; the engine surfaces it.
        var schema = new Schema { new SchemaElement("title", "h1") };
        var observed = new List<AgentDecisionOutcome>();
        var brain = new CapturingBrain(observed,
            new AgentDecision.Extract(schema) { Reason = "extract" },
            new AgentDecision.Stop { Reason = "give up" });
        var emptyExtractor = new EmptyObjectExtractor();

        var engine = await AgentEngineBuilder
            .Start("https://example.com/", "post-empty-extract outcome")
            .WithBrain(brain)
            .WithPageLoader(new FakeLoader("<html><body><p>no title here</p></body></html>"))
            .WithContentExtractor(emptyExtractor)
            .BuildAsync();

        await engine.RunAsync();

        Assert.Equal(2, observed.Count);
        var failed = Assert.IsType<AgentDecisionOutcome.Failed>(observed[1]);
        Assert.StartsWith("validation:", failed.Reason);
        Assert.Null(failed.ExceptionType);
    }

    [Fact]
    public async Task Custom_schema_validator_verdict_surfaces_in_LastOutcome_Failed_reason()
    {
        // ADR-0062 seam integration on the agent path. A consumer-supplied
        // ISchemaValidator with a specific Reason flows through to the
        // engine's LastOutcome.Failed.Reason — the brain sees the policy
        // verdict, not just "validation: schema not satisfied". Tests that
        // the validator's verdict propagates verbatim (post the
        // "validation: " prefix the engine adds).
        var schema = new Schema { new SchemaElement("title", "h1") };
        var observed = new List<AgentDecisionOutcome>();
        var brain = new CapturingBrain(observed,
            new AgentDecision.Extract(schema) { Reason = "extract" },
            new AgentDecision.Stop { Reason = "give up" });

        var rejectingValidator = new AlwaysRejectValidator("custom policy: at least 5 records");
        var extractor = new EmptyObjectExtractor();

        var engine = await AgentEngineBuilder
            .Start("https://example.com/", "validator pin")
            .WithBrain(brain)
            .WithPageLoader(new FakeLoader("<html><body><p>x</p></body></html>"))
            .WithContentExtractor(extractor)
            .WithSchemaValidator(rejectingValidator)
            .BuildAsync();

        await engine.RunAsync();

        var failed = Assert.IsType<AgentDecisionOutcome.Failed>(observed[1]);
        Assert.Equal("validation: custom policy: at least 5 records", failed.Reason);
        Assert.Null(failed.ExceptionType);
        Assert.True(rejectingValidator.Invoked, "Validator should have been consulted by the engine.");
    }

    [Fact]
    public async Task Null_extractor_output_surfaces_validation_Failed_without_consulting_validator()
    {
        // Defensive pre-check: SchemaSatisfiedValidator treats null record
        // as trivially valid (the no-schema/no-data posture). The agent
        // path needs null to fail loudly so the brain can revise — the
        // engine pre-checks and bypasses the validator on null.
        var schema = new Schema { new SchemaElement("title", "h1") };
        var observed = new List<AgentDecisionOutcome>();
        var brain = new CapturingBrain(observed,
            new AgentDecision.Extract(schema) { Reason = "extract" },
            new AgentDecision.Stop { Reason = "give up" });

        // A validator that would say "valid" on every input — must be
        // bypassed by the null pre-check.
        var permissiveValidator = new AlwaysAcceptValidator();
        var nullExtractor = new NullReturningExtractor();

        var engine = await AgentEngineBuilder
            .Start("https://example.com/", "null pin")
            .WithBrain(brain)
            .WithPageLoader(new FakeLoader("<html/>"))
            .WithContentExtractor(nullExtractor)
            .WithSchemaValidator(permissiveValidator)
            .BuildAsync();

        await engine.RunAsync();

        var failed = Assert.IsType<AgentDecisionOutcome.Failed>(observed[1]);
        Assert.Equal("validation: extractor returned null", failed.Reason);
        Assert.False(permissiveValidator.Invoked,
            "Validator should NOT be consulted when extractor returned null — the pre-check fires first.");
    }

    [Fact]
    public async Task After_successful_Follow_next_step_sees_Followed_outcome()
    {
        var observed = new List<AgentDecisionOutcome>();
        var brain = new CapturingBrain(observed,
            new AgentDecision.Follow("https://example.com/p2") { Reason = "next" },
            new AgentDecision.Stop { Reason = "done" });

        var engine = await AgentEngineBuilder
            .Start("https://example.com/", "post-follow outcome")
            .WithBrain(brain)
            .WithPageLoader(new FakeLoader("<html/>"))
            .BuildAsync();

        await engine.RunAsync();

        Assert.Equal(2, observed.Count);
        var followed = Assert.IsType<AgentDecisionOutcome.Followed>(observed[1]);
        Assert.Equal("https://example.com/p2", followed.ActualUrl);
        Assert.Equal(200, followed.StatusCode); // Static page → 200
    }

    [Fact]
    public async Task Follow_to_visited_url_surfaces_Failed_outcome_and_engine_does_NOT_terminate()
    {
        // Brain re-proposes the start URL. The engine rejects it and surfaces
        // a Failed("already visited", null) on the next step — and the loop
        // continues (the brain decides Stop).
        var observed = new List<AgentDecisionOutcome>();
        var brain = new CapturingBrain(observed,
            new AgentDecision.Follow("https://example.com/") { Reason = "revisit" },
            new AgentDecision.Stop { Reason = "give up" });

        var engine = await AgentEngineBuilder
            .Start("https://example.com/", "visited-rejection outcome")
            .WithBrain(brain)
            .WithPageLoader(new FakeLoader("<html/>"))
            .BuildAsync();

        var result = await engine.RunAsync();

        Assert.Equal(2, observed.Count);
        var failed = Assert.IsType<AgentDecisionOutcome.Failed>(observed[1]);
        Assert.Equal("already visited: https://example.com/", failed.Reason);
        Assert.Null(failed.ExceptionType);
        Assert.Equal("give up", result.TerminationReason);
    }

    [Fact]
    public async Task After_Act_decision_next_step_sees_ActDispatched_outcome()
    {
        var click = new PageAction.Click(".buy");
        var observed = new List<AgentDecisionOutcome>();
        var brain = new CapturingBrain(observed,
            new AgentDecision.Act(click) { Reason = "click buy" },
            new AgentDecision.Stop { Reason = "done" });

        var engine = await AgentEngineBuilder
            .Start("https://example.com/", "post-act outcome")
            .WithBrain(brain)
            .WithPageLoader(new FakeLoader("<html/>"))
            .BuildAsync();

        await engine.RunAsync();

        Assert.Equal(2, observed.Count);
        var dispatched = Assert.IsType<AgentDecisionOutcome.ActDispatched>(observed[1]);
        Assert.Same(click, dispatched.ResolvedAction);
    }

    [Fact]
    public async Task Page_load_failure_surfaces_Failed_outcome_and_engine_does_NOT_terminate()
    {
        // ADR-0061 behaviour change vs ADR-0051: page-load failures no longer
        // terminate the run; they surface as Failed outcomes the brain reads
        // on the *next* step (here: a Follow to a "bad" URL fails to load,
        // and the brain decides Stop on the next call).
        var observed = new List<AgentDecisionOutcome>();
        var brain = new CapturingBrain(observed,
            new AgentDecision.Follow("https://example.com/p2") { Reason = "next" },
            new AgentDecision.Stop { Reason = "give up on bad page" });
        var loader = new FailingFollowLoader(badUrl: "https://example.com/p2");

        var engine = await AgentEngineBuilder
            .Start("https://example.com/", "load-failure outcome")
            .WithBrain(brain)
            .WithPageLoader(loader)
            .BuildAsync();

        var result = await engine.RunAsync();

        // Both decisions happened — the load failure did NOT terminate.
        Assert.Equal(2, observed.Count);
        Assert.IsType<AgentDecisionOutcome.None>(observed[0]);
        var failed = Assert.IsType<AgentDecisionOutcome.Failed>(observed[1]);
        Assert.StartsWith("load:", failed.Reason);
        Assert.NotNull(failed.ExceptionType);
        Assert.Equal("give up on bad page", result.TerminationReason);
    }

    [Fact]
    public async Task Extract_exception_surfaces_Failed_outcome_and_engine_continues()
    {
        // An extractor that throws mid-Extract surfaces as Failed("extract: ...").
        var schema = new Schema { new SchemaElement("title", "h1") };
        var observed = new List<AgentDecisionOutcome>();
        var brain = new CapturingBrain(observed,
            new AgentDecision.Extract(schema) { Reason = "extract" },
            new AgentDecision.Stop { Reason = "done" });
        var extractor = new ThrowingExtractor();

        var engine = await AgentEngineBuilder
            .Start("https://example.com/", "extract-exception outcome")
            .WithBrain(brain)
            .WithPageLoader(new FakeLoader("<html><body><h1>X</h1></body></html>"))
            .WithContentExtractor(extractor)
            .BuildAsync();

        await engine.RunAsync();

        Assert.Equal(2, observed.Count);
        var failed = Assert.IsType<AgentDecisionOutcome.Failed>(observed[1]);
        Assert.StartsWith("extract:", failed.Reason);
        Assert.Equal("InvalidOperationException", failed.ExceptionType);
    }

    [Fact]
    public async Task Persisted_snapshot_carries_LastOutcome_field()
    {
        // The engine writes LastOutcome into the snapshot — pinning the
        // resume-time contract: a resumed run's first DecideAsync sees the
        // persisted outcome on AgentState.LastOutcome.
        var schema = new Schema { new SchemaElement("title", "h1") };
        var store = new RecordingStore();
        var brain = new ScriptedBrain(
            new AgentDecision.Extract(schema) { Reason = "row" },
            new AgentDecision.Stop { Reason = "done" });
        var loader = new FakeLoader("<html><body><h1>Hello</h1></body></html>");

        var engine = await AgentEngineBuilder
            .Start("https://example.com/", "snapshot LastOutcome")
            .WithBrain(brain)
            .WithPageLoader(loader)
            .WithRunStore(store)
            .BuildAsync();

        await engine.RunAsync();

        // First persist (Extract): the LastOutcome is None (no prior step).
        // Second persist (Stop): the LastOutcome is Extracted (the prior Extract's outcome).
        Assert.Equal(2, store.Saves.Count);
        Assert.IsType<AgentDecisionOutcome.None>(store.Saves[0].post.LastOutcome);
        var second = Assert.IsType<AgentDecisionOutcome.Extracted>(store.Saves[1].post.LastOutcome);
        Assert.Equal(1, second.RecordCount);
    }

    [Fact]
    public async Task Resumed_run_first_decision_sees_persisted_LastOutcome()
    {
        var store = new InMemoryAgentRunStore();
        var runId = "resume-outcome";

        // Seed the store with a snapshot whose LastOutcome is Followed.
        var seedOutcome = new AgentDecisionOutcome.Followed("https://example.com/", 200);
        var prior = new AgentRunSnapshot(
            Goal: "test resume outcome",
            LastDecidedStep: 0,
            History: new[] { new AgentDecision.Follow("https://example.com/") { Reason = "prior" } },
            VisitedUrls: new[] { "https://example.com/" },
            Records: Array.Empty<JsonObject>(),
            CurrentUrl: "https://example.com/",
            LastOutcome: seedOutcome);
        await store.SaveStepAsync(runId,
            new AgentDecision.Follow("https://example.com/") { Reason = "prior" }, prior);

        var observed = new List<AgentDecisionOutcome>();
        var brain = new CapturingBrain(observed, new AgentDecision.Stop { Reason = "ok" });

        var engine = await AgentEngineBuilder
            .Start("https://example.com/", "resume outcome")
            .WithBrain(brain)
            .WithPageLoader(new FakeLoader("<html/>"))
            .WithRunStore(store)
            .WithRunId(runId)
            .BuildAsync();

        await engine.RunAsync();

        // The resumed brain saw the persisted Followed outcome immediately.
        Assert.Single(observed);
        var followed = Assert.IsType<AgentDecisionOutcome.Followed>(observed[0]);
        Assert.Equal("https://example.com/", followed.ActualUrl);
        Assert.Equal(200, followed.StatusCode);
    }

    [Fact]
    public async Task Durable_sink_on_an_agent_run_is_warmed_and_flushed_on_dispose()
    {
        // ADR-0076 bug fix: pre-0076 the Agent driver was neither
        // IAsyncInitializable nor IAsyncDisposable, so a durable sink (Redis /
        // Mongo / Cosmos, or a buffered file drain) never ran its
        // InitializeAsync and never got its flush-on-dispose — silent record
        // loss on agent runs. The engine now warms its sinks at RunAsync and
        // flushes them on DisposeAsync (via the Post-extraction pipeline).
        var schema = new Schema { new SchemaElement("title", "h1") };
        var brain = new ScriptedBrain(
            new AgentDecision.Extract(schema) { Reason = "extract" },
            new AgentDecision.Stop { Reason = "done" });
        var loader = new FakeLoader("<html><body><h1>Hello</h1></body></html>");
        var sink = new DurableSink();

        await using (var engine = await AgentEngineBuilder
            .Start("https://example.com/", "get the title")
            .WithBrain(brain)
            .WithPageLoader(loader)
            .AddSink(sink)
            .BuildAsync())
        {
            await engine.RunAsync();

            // Warmed exactly once at RunAsync; the record reached the sink.
            Assert.Equal(1, sink.Inits);
            Assert.Single(sink.Emitted);
            // Not yet disposed — still inside the using scope.
            Assert.Equal(0, sink.Disposes);
        }

        // Flushed on scope exit (engine.DisposeAsync → pipeline → sink).
        Assert.Equal(1, sink.Disposes);
    }

    // --------------- fakes ---------------

    // ADR-0076: a durable sink declares the warm-up + dispose capabilities the
    // pre-0076 agent path silently skipped.
    private sealed class DurableSink : IScraperSink, IAsyncInitializable, IAsyncDisposable
    {
        public int Inits;
        public int Disposes;
        public List<ParsedData> Emitted { get; } = new();
        public bool DataCleanupOnStart { get; set; }
        public Task InitializeAsync() { Inits++; return Task.CompletedTask; }
        public Task EmitAsync(ParsedData entity, CancellationToken ct = default)
        {
            Emitted.Add(entity);
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync() { Disposes++; return ValueTask.CompletedTask; }
    }

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

    // ADR-0061: captures every AgentState.LastOutcome the engine surfaces
    // to the brain, in step order. Pins the first-step-None,
    // post-Extract-Extracted, post-Follow-Followed contracts.
    private sealed class CapturingBrain : IAgentBrain
    {
        private readonly List<AgentDecisionOutcome> _captureTo;
        private readonly Queue<AgentDecision> _decisions;

        public CapturingBrain(List<AgentDecisionOutcome> captureTo, params AgentDecision[] scripted)
        {
            _captureTo = captureTo;
            _decisions = new(scripted);
        }

        public ValueTask<AgentDecision> DecideAsync(AgentState state, CancellationToken ct = default)
        {
            _captureTo.Add(state.LastOutcome);
            return ValueTask.FromResult(_decisions.Count > 0
                ? _decisions.Dequeue()
                : new AgentDecision.Stop { Reason = "capturing brain exhausted" });
        }
    }

    // Loader that loads the start URL fine and throws on the bad URL —
    // pins the load-failure-non-terminal behaviour (ADR-0061 behaviour
    // change vs ADR-0051).
    private sealed class FailingFollowLoader(string badUrl) : IPageLoader
    {
        public Task<PageLoadResult> LoadAsync(PageRequest request, CancellationToken ct = default)
        {
            if (request.Url == badUrl)
                throw new HttpRequestException($"simulated load failure on {request.Url}");
            return Task.FromResult(new PageLoadResult { Html = "<html/>" });
        }
    }

    // Throws synchronously inside the Extract execution path so the engine
    // surfaces a Failed("extract: ...", "InvalidOperationException") outcome
    // for the *next* step.
    private sealed class ThrowingExtractor : IContentExtractor
    {
        public Task<JsonObject> ExtractAsync(string document, Schema? schema)
            => throw new InvalidOperationException("simulated extractor failure");
    }

    // Returns the empty object `{}` for every page — the SchemaSatisfiedValidator
    // (the ADR-0062 default the engine now consults) treats this as a
    // validation failure because required leaves are absent; the engine
    // surfaces Failed("validation: ...") in LastOutcome.
    private sealed class EmptyObjectExtractor : IContentExtractor
    {
        public Task<JsonObject> ExtractAsync(string document, Schema? schema)
            => Task.FromResult(new JsonObject());
    }

    // Returns null — exercises the engine's null pre-check (the
    // SchemaSatisfiedValidator default treats null as trivially valid,
    // which is wrong for the agent path; the engine pre-checks).
    private sealed class NullReturningExtractor : IContentExtractor
    {
        public Task<JsonObject> ExtractAsync(string document, Schema? schema)
            => Task.FromResult<JsonObject>(null!);
    }

    // A stub ISchemaValidator that rejects every input with a fixed reason
    // — pins that the agent surfaces the validator's verdict verbatim.
    private sealed class AlwaysRejectValidator(string reason) : ISchemaValidator
    {
        public bool Invoked { get; private set; }

        public ValidationResult Validate(JsonObject? extracted, Schema? schema)
        {
            Invoked = true;
            return ValidationResult.Invalid(reason);
        }
    }

    // A stub ISchemaValidator that accepts every input — used to prove the
    // engine bypasses the validator on null extractions (the validator must
    // not be invoked when there's no record to check).
    private sealed class AlwaysAcceptValidator : ISchemaValidator
    {
        public bool Invoked { get; private set; }

        public ValidationResult Validate(JsonObject? extracted, Schema? schema)
        {
            Invoked = true;
            return ValidationResult.Valid;
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
        public Task<PageLoadResult> LoadAsync(PageRequest request, CancellationToken ct = default)
            => Task.FromResult(new PageLoadResult { Html = html });
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
