using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using WebReaper.Domain.PageActions;
using WebReaper.Serialization.Codecs;

namespace WebReaper.Serialization.Converters;

/// <summary>
/// <see cref="PageAction"/> codec (ADR 0008, ADR-0035). Since ADR-0035
/// <c>PageAction</c> is a closed sum of typed arms, not a
/// <c>(PageActionType, object[])</c> pair — so the codec is a <c>type</c>
/// discriminator plus the arm's typed fields, and the pre-0035 per-value
/// kind-tagging (the workaround for a genuinely-polymorphic <c>object[]</c>)
/// is gone.
/// <para>
/// Migrated to the shared <see cref="ClosedSumCodec{T}"/> mechanism (ADR-0077):
/// each arm is one <c>(tag, write, build)</c> row instead of a hand-written
/// write switch + read loop + read switch. The static <see cref="Write"/> /
/// <see cref="Read"/> facade stays byte-identical so callers (the converter
/// wrapper, the selector-chain streaming reader) are unchanged; <see cref="From"/>
/// is the composition seam a parent sum uses for a nested action.
/// </para>
/// </summary>
internal static class PageActionCodec
{
    private static readonly ClosedSumCodec<PageAction> Codec = new(
        "PageAction",
        [
            ClosedSumCodec<PageAction>.Arm<PageAction.Click>(
                "click",
                (w, x) => w.WriteString("selector", x.Selector),
                ctx => new PageAction.Click(ctx.Require("selector"))),
            ClosedSumCodec<PageAction>.Arm<PageAction.Wait>(
                "wait",
                (w, x) => w.WriteNumber("ms", x.Milliseconds),
                ctx => new PageAction.Wait(ctx.OptionalInt("ms"))),
            ClosedSumCodec<PageAction>.Arm<PageAction.ScrollToEnd>(
                "scrollToEnd", () => new PageAction.ScrollToEnd()),
            ClosedSumCodec<PageAction>.Arm<PageAction.EvaluateExpression>(
                "evaluateExpression",
                (w, x) => w.WriteString("expression", x.Expression),
                ctx => new PageAction.EvaluateExpression(ctx.Require("expression"))),
            ClosedSumCodec<PageAction>.Arm<PageAction.WaitForSelector>(
                "waitForSelector",
                (w, x) =>
                {
                    w.WriteString("selector", x.Selector);
                    w.WriteNumber("timeoutMs", x.TimeoutMs);
                },
                ctx => new PageAction.WaitForSelector(ctx.Require("selector"), ctx.OptionalInt("timeoutMs"))),
            ClosedSumCodec<PageAction>.Arm<PageAction.WaitForNetworkIdle>(
                "waitForNetworkIdle", () => new PageAction.WaitForNetworkIdle()),
            ClosedSumCodec<PageAction>.Arm<PageAction.ScrollIntoView>(
                "scrollIntoView",
                (w, x) => w.WriteString("selector", x.Selector),
                ctx => new PageAction.ScrollIntoView(ctx.Require("selector"))),
            // ADR-0050: persisted as the intent string only — the resolved arm
            // is a per-crawl runtime concern and is intentionally never
            // persisted (it would freeze the LLM's selector across crawls,
            // defeating the resolve-on-cache-miss recovery path).
            ClosedSumCodec<PageAction>.Arm<PageAction.SemanticAct>(
                "semanticAct",
                (w, x) => w.WriteString("intent", x.Intent),
                ctx => new PageAction.SemanticAct(ctx.Require("intent"))),
            // ADR-0074: key string is the only field; no selector.
            ClosedSumCodec<PageAction>.Arm<PageAction.Press>(
                "press",
                (w, x) => w.WriteString("key", x.Key),
                ctx => new PageAction.Press(ctx.Require("key"))),
            // ADR-0074: wire tag "fill" with selector + value fields.
            ClosedSumCodec<PageAction>.Arm<PageAction.Fill>(
                "fill",
                (w, x) =>
                {
                    w.WriteString("selector", x.Selector);
                    w.WriteString("value", x.Value);
                },
                ctx => new PageAction.Fill(ctx.Require("selector"), ctx.Require("value"))),
        ]);

    public static void Write(Utf8JsonWriter w, PageAction a) => Codec.Write(w, a);

    public static PageAction Read(ref Utf8JsonReader r) => Codec.Read(ref r);

    /// <summary>Build a <see cref="PageAction"/> from an already-materialized
    /// node — the composition seam <c>AgentDecision.Act</c> and
    /// <c>AgentDecisionOutcome.ActDispatched</c> use for their nested action
    /// (ADR-0077).</summary>
    public static PageAction From(JsonNode node) => Codec.From(node);
}

internal sealed class PageActionJsonConverter : JsonConverter<PageAction>
{
    public override PageAction Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o)
        => PageActionCodec.Read(ref reader);

    public override void Write(Utf8JsonWriter writer, PageAction value, JsonSerializerOptions o)
        => PageActionCodec.Write(writer, value);
}
