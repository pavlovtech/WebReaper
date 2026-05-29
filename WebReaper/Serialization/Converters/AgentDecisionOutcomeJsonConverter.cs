using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using WebReaper.Domain.Agent;
using WebReaper.Domain.PageActions;
using WebReaper.Serialization.Codecs;

namespace WebReaper.Serialization.Converters;

/// <summary>
/// <see cref="AgentDecisionOutcome"/> codec (ADR-0061). A <c>type</c>
/// discriminator plus the arm's typed fields.
/// <para>
/// Migrated to the shared <see cref="ClosedSumCodec{T}"/> mechanism (ADR-0077).
/// The Extracted arm carries an optional <see cref="JsonObject"/> record
/// (cloned detached on read, matching the prior fresh-parse), and the
/// ActDispatched arm composes <see cref="PageActionCodec.From"/> for its nested
/// action while keeping its bespoke missing-field message. The static
/// <see cref="Write"/> / <see cref="Read"/> facade stays byte-identical so the
/// agent snapshot codec's streaming reader is unchanged.
/// </para>
/// </summary>
internal static class AgentDecisionOutcomeCodec
{
    private static readonly ClosedSumCodec<AgentDecisionOutcome> Codec = new(
        "AgentDecisionOutcome",
        [
            ClosedSumCodec<AgentDecisionOutcome>.Arm<AgentDecisionOutcome.None>(
                "none", () => new AgentDecisionOutcome.None()),
            ClosedSumCodec<AgentDecisionOutcome>.Arm<AgentDecisionOutcome.Extracted>(
                "extracted",
                (w, x) =>
                {
                    w.WriteNumber("recordCount", x.RecordCount);
                    if (x.Record is not null)
                    {
                        w.WritePropertyName("record");
                        x.Record.WriteTo(w);
                    }
                },
                ctx => new AgentDecisionOutcome.Extracted(
                    ctx.OptionalObjectClone("record"), ctx.OptionalInt("recordCount"))),
            ClosedSumCodec<AgentDecisionOutcome>.Arm<AgentDecisionOutcome.Followed>(
                "followed",
                (w, x) =>
                {
                    w.WriteString("actualUrl", x.ActualUrl);
                    w.WriteNumber("statusCode", x.StatusCode);
                },
                ctx => new AgentDecisionOutcome.Followed(
                    ctx.Require("actualUrl"), ctx.OptionalInt("statusCode"))),
            ClosedSumCodec<AgentDecisionOutcome>.Arm<AgentDecisionOutcome.ActDispatched>(
                "actDispatched",
                (w, x) =>
                {
                    w.WritePropertyName("resolvedAction");
                    PageActionCodec.Write(w, x.ResolvedAction);
                },
                ctx => new AgentDecisionOutcome.ActDispatched(
                    ctx.OptionalChild("resolvedAction", PageActionCodec.From)
                    ?? throw new JsonException("AgentDecisionOutcome.ActDispatched missing 'resolvedAction'"))),
            ClosedSumCodec<AgentDecisionOutcome>.Arm<AgentDecisionOutcome.Failed>(
                "failed",
                (w, x) =>
                {
                    w.WriteString("reason", x.Reason);
                    if (x.ExceptionType is not null)
                        w.WriteString("exceptionType", x.ExceptionType);
                },
                ctx => new AgentDecisionOutcome.Failed(
                    ctx.Require("reason"), ctx.OptionalString("exceptionType"))),
            ClosedSumCodec<AgentDecisionOutcome>.Arm<AgentDecisionOutcome.Stopped>(
                "stopped",
                (w, x) => w.WriteString("reason", x.Reason),
                ctx => new AgentDecisionOutcome.Stopped(ctx.Require("reason"))),
        ]);

    public static void Write(Utf8JsonWriter w, AgentDecisionOutcome o) => Codec.Write(w, o);

    public static AgentDecisionOutcome Read(ref Utf8JsonReader r) => Codec.Read(ref r);

    /// <summary>Build an <see cref="AgentDecisionOutcome"/> from an
    /// already-materialized node — the composition seam the agent snapshot codec
    /// could use for the LastOutcome field (ADR-0077).</summary>
    public static AgentDecisionOutcome From(JsonNode node) => Codec.From(node);
}

internal sealed class AgentDecisionOutcomeJsonConverter : JsonConverter<AgentDecisionOutcome>
{
    public override AgentDecisionOutcome Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o)
        => AgentDecisionOutcomeCodec.Read(ref reader);

    public override void Write(Utf8JsonWriter writer, AgentDecisionOutcome value, JsonSerializerOptions o)
        => AgentDecisionOutcomeCodec.Write(writer, value);
}
