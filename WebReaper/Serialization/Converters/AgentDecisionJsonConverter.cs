using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using WebReaper.Domain.Agent;
using WebReaper.Domain.Parsing;
using WebReaper.Serialization.Codecs;

namespace WebReaper.Serialization.Converters;

/// <summary>
/// <see cref="AgentDecision"/> codec (ADR-0051). A <c>type</c> discriminator
/// plus the arm's typed fields, with <c>reason</c> as the common field on every
/// arm — written BEFORE the discriminator, a byte-order the persisted snapshots
/// depend on.
/// <para>
/// Migrated to the shared <see cref="ClosedSumCodec{T}"/> mechanism (ADR-0077).
/// The Extract arm composes <see cref="SchemaCodec.From"/> for its nested Schema
/// and the Act arm composes <see cref="PageActionCodec.From"/> for its nested
/// action — manual recursion via the mechanism's materialized child seam, no
/// reflection (AOT-trivially-safe). The static <see cref="Write"/> /
/// <see cref="Read"/> facade stays byte-identical so streaming callers (the
/// agent snapshot codec) are unchanged.
/// </para>
/// </summary>
internal static class AgentDecisionCodec
{
    private static readonly ClosedSumCodec<AgentDecision> Codec = new(
        "AgentDecision",
        [
            ClosedSumCodec<AgentDecision>.Arm<AgentDecision.Extract>(
                "extract",
                (w, x) =>
                {
                    w.WritePropertyName("schema");
                    SchemaCodec.Write(w, x.Schema);
                },
                ctx => new AgentDecision.Extract(ReadSchema(ctx)) { Reason = ctx.Common("reason") }),
            ClosedSumCodec<AgentDecision>.Arm<AgentDecision.Follow>(
                "follow",
                (w, x) => w.WriteString("url", x.Url),
                ctx => new AgentDecision.Follow(ctx.Require("url")) { Reason = ctx.Common("reason") }),
            ClosedSumCodec<AgentDecision>.Arm<AgentDecision.Act>(
                "act",
                (w, x) =>
                {
                    w.WritePropertyName("action");
                    PageActionCodec.Write(w, x.Action);
                },
                ctx => new AgentDecision.Act(ctx.RequireChild("action", PageActionCodec.From))
                {
                    Reason = ctx.Common("reason")
                }),
            ClosedSumCodec<AgentDecision>.Arm<AgentDecision.Stop>(
                "stop",
                (_, _) => { },
                ctx => new AgentDecision.Stop { Reason = ctx.Common("reason") }),
        ],
        // The common field every arm shares, written before the discriminator.
        writeCommon: (w, d) => w.WriteString("reason", d.Reason));

    public static void Write(Utf8JsonWriter w, AgentDecision d) => Codec.Write(w, d);

    public static AgentDecision Read(ref Utf8JsonReader r) => Codec.Read(ref r);

    /// <summary>Build an <see cref="AgentDecision"/> from an already-materialized
    /// node — the composition seam the agent snapshot codec could use for a
    /// history entry (ADR-0077).</summary>
    public static AgentDecision From(JsonNode node) => Codec.From(node);

    // Extract's schema is a Schema container, never a leaf SchemaElement; the
    // cast preserves the pre-migration error message verbatim.
    private static Schema ReadSchema(ArmReaderContext ctx)
        => ctx.RequireChild("schema", SchemaCodec.From) as Schema
           ?? throw new JsonException(
               "AgentDecision.Extract schema must be a Schema container, not a leaf SchemaElement");
}

internal sealed class AgentDecisionJsonConverter : JsonConverter<AgentDecision>
{
    public override AgentDecision Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o)
        => AgentDecisionCodec.Read(ref reader);

    public override void Write(Utf8JsonWriter writer, AgentDecision value, JsonSerializerOptions o)
        => AgentDecisionCodec.Write(writer, value);
}
