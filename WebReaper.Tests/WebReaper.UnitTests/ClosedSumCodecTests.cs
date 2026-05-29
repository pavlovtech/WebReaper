using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WebReaper.Domain.Agent;
using WebReaper.Domain.PageActions;
using WebReaper.Domain.Parsing;
using WebReaper.Serialization.Converters;

namespace WebReaper.UnitTests;

// ADR-0077: the three flat closed sums (PageAction, AgentDecision,
// AgentDecisionOutcome) serialize through the shared ClosedSumCodec mechanism.
// These are characterization tests written against the wire format and kept
// green across the migration. Byte-stability is load-bearing (persisted agent
// snapshots and configs must round-trip), so the exact-bytes tests pin field
// order (type-first for PageAction / AgentDecisionOutcome, reason-before-type
// for AgentDecision) and the round-trips pin every arm. They exercise the
// public *Codec.Write / Read facades, which stay byte-identical through the
// migration.
public class ClosedSumCodecTests
{
    // ---- exact wire format (field order is load-bearing) -------------------

    [Fact]
    public void PageAction_writes_type_first()
        => Assert.Equal(
            """{"type":"waitForSelector","selector":".m","timeoutMs":42}""",
            WritePageAction(new PageAction.WaitForSelector(".m", 42)));

    [Fact]
    public void AgentDecision_writes_reason_before_type()
        // The ADR pins this ordering: reason is the common field, emitted
        // before the discriminator. Persisted snapshots depend on it.
        => Assert.Equal(
            """{"reason":"because","type":"follow","url":"https://x/"}""",
            WriteDecision(new AgentDecision.Follow("https://x/") { Reason = "because" }));

    [Fact]
    public void AgentDecisionOutcome_writes_type_first()
        => Assert.Equal(
            """{"type":"followed","actualUrl":"https://x/","statusCode":200}""",
            WriteOutcome(new AgentDecisionOutcome.Followed("https://x/", 200)));

    [Fact]
    public void Extracted_outcome_omits_record_when_null_but_keeps_count()
        => Assert.Equal(
            """{"type":"extracted","recordCount":2}""",
            WriteOutcome(new AgentDecisionOutcome.Extracted(null, 2)));

    // ---- round-trip every arm ----------------------------------------------

    [Fact]
    public void Every_PageAction_arm_round_trips()
    {
        PageAction[] arms =
        [
            new PageAction.Click(".c"),
            new PageAction.Wait(7),
            new PageAction.ScrollToEnd(),
            new PageAction.EvaluateExpression("1+1"),
            new PageAction.WaitForSelector(".s", 99),
            new PageAction.WaitForNetworkIdle(),
            new PageAction.ScrollIntoView(".v"),
            new PageAction.SemanticAct("do it"),
            new PageAction.Press("Enter"),
            new PageAction.Fill(".f", "val"),
        ];
        foreach (var a in arms)
            Assert.Equal(a, RoundTripPageAction(a)); // records: value equality
    }

    [Fact]
    public void Every_AgentDecision_arm_round_trips()
    {
        var schema = new Schema();
        schema.Add(new SchemaElement("title", "h1"));
        AgentDecision[] arms =
        [
            new AgentDecision.Extract(schema) { Reason = "r1" },
            new AgentDecision.Follow("https://x/") { Reason = "r2" },
            new AgentDecision.Act(new PageAction.Click(".a")) { Reason = "r3" },
            new AgentDecision.Stop { Reason = "r4" },
        ];
        foreach (var d in arms)
        {
            var rt = RoundTripDecision(d);
            Assert.Equal(d.GetType(), rt.GetType());
            Assert.Equal(d.Reason, rt.Reason);
        }

        // The Act arm carries its nested PageAction across the round-trip.
        var act = Assert.IsType<AgentDecision.Act>(RoundTripDecision(arms[2]));
        Assert.Equal(".a", Assert.IsType<PageAction.Click>(act.Action).Selector);
        // The Extract arm carries its nested Schema (a container, not a leaf).
        var ext = Assert.IsType<AgentDecision.Extract>(RoundTripDecision(arms[0]));
        Assert.Single(ext.Schema.Children);
    }

    // ---- unknown tag throws ------------------------------------------------

    [Fact]
    public void PageAction_unknown_tag_throws()
        => Assert.Throws<JsonException>(() => ReadPageAction("""{"type":"nope"}"""));

    [Fact]
    public void AgentDecision_unknown_tag_throws()
        => Assert.Throws<JsonException>(() => ReadDecision("""{"reason":"r","type":"nope"}"""));

    [Fact]
    public void AgentDecisionOutcome_unknown_tag_throws()
        => Assert.Throws<JsonException>(() => ReadOutcome("""{"type":"nope"}"""));

    // ---- missing-field messages --------------------------------------------

    [Fact]
    public void PageAction_missing_required_field_names_the_sum_arm_and_field()
    {
        var ex = Assert.Throws<JsonException>(() => ReadPageAction("""{"type":"click"}"""));
        Assert.Equal("PageAction 'click' is missing required 'selector'", ex.Message);
    }

    [Fact]
    public void AgentDecisionOutcome_actDispatched_missing_action_preserves_its_message()
    {
        // ActDispatched uses a bespoke message, not the shared Require format —
        // pinned so the migration preserves it verbatim.
        var ex = Assert.Throws<JsonException>(() => ReadOutcome("""{"type":"actDispatched"}"""));
        Assert.Equal("AgentDecisionOutcome.ActDispatched missing 'resolvedAction'", ex.Message);
    }

    // ---- helpers (the public codec facades, stable across the migration) ----

    private static string WritePageAction(PageAction a) => Write(w => PageActionCodec.Write(w, a));
    private static string WriteDecision(AgentDecision d) => Write(w => AgentDecisionCodec.Write(w, d));
    private static string WriteOutcome(AgentDecisionOutcome o) => Write(w => AgentDecisionOutcomeCodec.Write(w, o));

    private static PageAction RoundTripPageAction(PageAction a) => ReadPageAction(WritePageAction(a));
    private static AgentDecision RoundTripDecision(AgentDecision d) => ReadDecision(WriteDecision(d));

    private static PageAction ReadPageAction(string json) => Read(json, (ref Utf8JsonReader r) => PageActionCodec.Read(ref r));
    private static AgentDecision ReadDecision(string json) => Read(json, (ref Utf8JsonReader r) => AgentDecisionCodec.Read(ref r));
    private static AgentDecisionOutcome ReadOutcome(string json) => Read(json, (ref Utf8JsonReader r) => AgentDecisionOutcomeCodec.Read(ref r));

    private static string Write(Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream)) write(w);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private delegate T ReadFn<T>(ref Utf8JsonReader r);

    private static T Read<T>(string json, ReadFn<T> read)
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        Assert.True(reader.Read());
        return read(ref reader);
    }
}
