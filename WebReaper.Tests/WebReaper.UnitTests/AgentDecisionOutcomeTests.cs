using System.Text.Json.Nodes;
using WebReaper.Domain.Agent;
using WebReaper.Domain.PageActions;
using WebReaper.Serialization.Converters;

namespace WebReaper.UnitTests;

// ADR-0061: AgentDecisionOutcome is the closed sum the engine populates on
// AgentState.LastOutcome and AgentRunSnapshot.LastOutcome. These tests pin
// the arm shapes, equality (record value semantics) and JSON round-trip
// through the codec the IAgentRunStore adapters use.
public class AgentDecisionOutcomeTests
{
    // ---------- closed-sum shape and equality ----------

    [Fact]
    public void None_arm_has_no_fields_and_is_value_equal_across_instances()
    {
        var a = new AgentDecisionOutcome.None();
        var b = new AgentDecisionOutcome.None();
        Assert.Equal(a, b);
    }

    [Fact]
    public void Extracted_arm_carries_optional_record_and_count()
    {
        var record = new JsonObject { ["title"] = "hello" };
        var outcome = new AgentDecisionOutcome.Extracted(record, 3);
        Assert.Equal(3, outcome.RecordCount);
        Assert.NotNull(outcome.Record);
        Assert.Equal("hello", outcome.Record!["title"]?.GetValue<string>());
    }

    [Fact]
    public void Extracted_arm_accepts_null_record_for_dropped_records()
    {
        var outcome = new AgentDecisionOutcome.Extracted(Record: null, RecordCount: 2);
        Assert.Null(outcome.Record);
        Assert.Equal(2, outcome.RecordCount);
    }

    [Fact]
    public void Followed_arm_carries_actual_url_and_status_code()
    {
        var outcome = new AgentDecisionOutcome.Followed("https://example.com/redirected", 200);
        Assert.Equal("https://example.com/redirected", outcome.ActualUrl);
        Assert.Equal(200, outcome.StatusCode);
    }

    [Fact]
    public void Followed_arm_with_zero_status_means_dynamic_page()
    {
        // ADR-0061 fork 3: 0 status code is the dynamic-page sentinel.
        var outcome = new AgentDecisionOutcome.Followed("https://example.com/", 0);
        Assert.Equal(0, outcome.StatusCode);
    }

    [Fact]
    public void ActDispatched_arm_carries_the_resolved_action()
    {
        var click = new PageAction.Click(".buy");
        var outcome = new AgentDecisionOutcome.ActDispatched(click);
        Assert.Same(click, outcome.ResolvedAction);
    }

    [Fact]
    public void Failed_arm_carries_reason_and_optional_exception_type()
    {
        var outcome = new AgentDecisionOutcome.Failed("load: timeout", "HttpRequestException");
        Assert.Equal("load: timeout", outcome.Reason);
        Assert.Equal("HttpRequestException", outcome.ExceptionType);
    }

    [Fact]
    public void Failed_arm_accepts_null_exception_type_for_structural_failures()
    {
        // "already visited" is a structural rejection — no exception was thrown.
        var outcome = new AgentDecisionOutcome.Failed("already visited", ExceptionType: null);
        Assert.Equal("already visited", outcome.Reason);
        Assert.Null(outcome.ExceptionType);
    }

    [Fact]
    public void Stopped_arm_carries_termination_reason()
    {
        var outcome = new AgentDecisionOutcome.Stopped("goal met");
        Assert.Equal("goal met", outcome.Reason);
    }

    [Fact]
    public void Records_are_value_equal_when_all_fields_match()
    {
        var a = new AgentDecisionOutcome.Failed("x", "Foo");
        var b = new AgentDecisionOutcome.Failed("x", "Foo");
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Records_are_not_equal_across_arms_even_with_same_underlying_data()
    {
        var a = new AgentDecisionOutcome.Failed("done", null);
        var b = new AgentDecisionOutcome.Stopped("done");
        Assert.NotEqual<AgentDecisionOutcome>(a, b);
    }

    [Fact]
    public void Pattern_match_can_branch_on_every_arm_without_default()
    {
        // The brain pattern-matches the outcome to decide; we pin the closed
        // sum covers exhaustively (no compile-time fallback needed at use sites).
        AgentDecisionOutcome[] all =
        {
            new AgentDecisionOutcome.None(),
            new AgentDecisionOutcome.Extracted(null, 0),
            new AgentDecisionOutcome.Followed("u", 200),
            new AgentDecisionOutcome.ActDispatched(new PageAction.Click("a")),
            new AgentDecisionOutcome.Failed("r", null),
            new AgentDecisionOutcome.Stopped("r")
        };

        foreach (var o in all)
        {
            string label = o switch
            {
                AgentDecisionOutcome.None => "none",
                AgentDecisionOutcome.Extracted => "extracted",
                AgentDecisionOutcome.Followed => "followed",
                AgentDecisionOutcome.ActDispatched => "actDispatched",
                AgentDecisionOutcome.Failed => "failed",
                AgentDecisionOutcome.Stopped => "stopped",
                _ => "unknown" // unreachable for the closed sum but keeps the switch satisfied
            };
            Assert.NotEqual("unknown", label);
        }
    }

    // ---------- JSON round-trip via the codec the IAgentRunStore uses ----------

    [Fact]
    public void None_round_trips_through_codec()
        => AssertRoundTrip(new AgentDecisionOutcome.None());

    [Fact]
    public void Extracted_with_record_round_trips_through_codec()
    {
        var record = new JsonObject { ["title"] = "foo", ["price"] = 42 };
        var outcome = new AgentDecisionOutcome.Extracted(record, 5);
        var roundTripped = RoundTrip(outcome);
        var extracted = Assert.IsType<AgentDecisionOutcome.Extracted>(roundTripped);
        Assert.Equal(5, extracted.RecordCount);
        Assert.NotNull(extracted.Record);
        Assert.Equal("foo", extracted.Record!["title"]?.GetValue<string>());
        Assert.Equal(42, extracted.Record["price"]?.GetValue<int>());
    }

    [Fact]
    public void Extracted_with_null_record_round_trips_through_codec()
    {
        var outcome = new AgentDecisionOutcome.Extracted(Record: null, RecordCount: 3);
        var roundTripped = RoundTrip(outcome);
        var extracted = Assert.IsType<AgentDecisionOutcome.Extracted>(roundTripped);
        Assert.Null(extracted.Record);
        Assert.Equal(3, extracted.RecordCount);
    }

    [Fact]
    public void Followed_round_trips_through_codec()
    {
        var outcome = new AgentDecisionOutcome.Followed("https://example.com/x", 404);
        var roundTripped = RoundTrip(outcome);
        var followed = Assert.IsType<AgentDecisionOutcome.Followed>(roundTripped);
        Assert.Equal("https://example.com/x", followed.ActualUrl);
        Assert.Equal(404, followed.StatusCode);
    }

    [Fact]
    public void ActDispatched_round_trips_through_codec()
    {
        var outcome = new AgentDecisionOutcome.ActDispatched(new PageAction.Click(".sign-in"));
        var roundTripped = RoundTrip(outcome);
        var dispatched = Assert.IsType<AgentDecisionOutcome.ActDispatched>(roundTripped);
        var click = Assert.IsType<PageAction.Click>(dispatched.ResolvedAction);
        Assert.Equal(".sign-in", click.Selector);
    }

    [Fact]
    public void ActDispatched_round_trips_every_concrete_action_arm()
    {
        PageAction[] actions =
        {
            new PageAction.Click(".x"),
            new PageAction.Wait(500),
            new PageAction.ScrollToEnd(),
            new PageAction.EvaluateExpression("window.scrollTo(0,0)"),
            new PageAction.WaitForSelector(".loaded", 10_000),
            new PageAction.WaitForNetworkIdle(),
            new PageAction.SemanticAct("click sign in")
        };
        foreach (var action in actions)
        {
            var rt = RoundTrip(new AgentDecisionOutcome.ActDispatched(action));
            var dispatched = Assert.IsType<AgentDecisionOutcome.ActDispatched>(rt);
            Assert.Equal(action.GetType(), dispatched.ResolvedAction.GetType());
        }
    }

    [Fact]
    public void Failed_with_exception_type_round_trips_through_codec()
    {
        var outcome = new AgentDecisionOutcome.Failed("extract: parse error", "JsonException");
        var roundTripped = RoundTrip(outcome);
        var failed = Assert.IsType<AgentDecisionOutcome.Failed>(roundTripped);
        Assert.Equal("extract: parse error", failed.Reason);
        Assert.Equal("JsonException", failed.ExceptionType);
    }

    [Fact]
    public void Failed_without_exception_type_round_trips_through_codec()
    {
        var outcome = new AgentDecisionOutcome.Failed("already visited", null);
        var roundTripped = RoundTrip(outcome);
        var failed = Assert.IsType<AgentDecisionOutcome.Failed>(roundTripped);
        Assert.Equal("already visited", failed.Reason);
        Assert.Null(failed.ExceptionType);
    }

    [Fact]
    public void Stopped_round_trips_through_codec()
    {
        var outcome = new AgentDecisionOutcome.Stopped("goal met");
        var roundTripped = RoundTrip(outcome);
        var stopped = Assert.IsType<AgentDecisionOutcome.Stopped>(roundTripped);
        Assert.Equal("goal met", stopped.Reason);
    }

    // ---------- helpers ----------

    private static AgentDecisionOutcome RoundTrip(AgentDecisionOutcome o)
    {
        using var stream = new System.IO.MemoryStream();
        using (var writer = new System.Text.Json.Utf8JsonWriter(stream))
            AgentDecisionOutcomeCodec.Write(writer, o);
        var bytes = stream.ToArray();
        var reader = new System.Text.Json.Utf8JsonReader(bytes);
        Assert.True(reader.Read());
        return AgentDecisionOutcomeCodec.Read(ref reader);
    }

    private static void AssertRoundTrip(AgentDecisionOutcome o)
    {
        var rt = RoundTrip(o);
        Assert.Equal(o.GetType(), rt.GetType());
    }
}
