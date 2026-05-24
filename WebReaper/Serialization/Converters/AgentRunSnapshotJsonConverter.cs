using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using WebReaper.Domain.Agent;
using WebReaper.Domain.PageActions;

namespace WebReaper.Serialization.Converters;

/// <summary>
/// <see cref="AgentRunSnapshot"/> codec (ADR-0051 §Decision §6, extended by
/// ADR-0061 for the <c>LastOutcome</c> field). Hand-written
/// Utf8JsonReader/Writer plumbing — AOT-safe (no reflection, no source-gen
/// metadata required for the snapshot's polymorphic <see cref="AgentDecision"/>
/// arms, its <see cref="JsonObject"/> record payloads, or its
/// <see cref="AgentDecisionOutcome"/> arms). Delegates to
/// <see cref="AgentDecisionCodec"/> for the History arms,
/// <see cref="AgentDecisionOutcomeCodec"/> for the LastOutcome arms, and
/// <see cref="JsonObject.WriteTo(Utf8JsonWriter, JsonSerializerOptions)"/> /
/// <see cref="JsonNode.Parse(ref Utf8JsonReader, JsonNodeOptions?)"/> for the
/// Records payloads.
/// <para>
/// Backward-compatible read: snapshots persisted by pre-ADR-0061 versions
/// omit <c>lastOutcome</c>; the reader defaults to
/// <see cref="AgentDecisionOutcome.None"/>.
/// </para>
/// </summary>
internal static class AgentRunSnapshotCodec
{
    public static void Write(Utf8JsonWriter w, AgentRunSnapshot s)
    {
        w.WriteStartObject();
        w.WriteString("goal", s.Goal);
        w.WriteNumber("lastDecidedStep", s.LastDecidedStep);

        w.WritePropertyName("history");
        w.WriteStartArray();
        foreach (var d in s.History) AgentDecisionCodec.Write(w, d);
        w.WriteEndArray();

        w.WritePropertyName("visitedUrls");
        w.WriteStartArray();
        foreach (var u in s.VisitedUrls) w.WriteStringValue(u);
        w.WriteEndArray();

        w.WritePropertyName("records");
        w.WriteStartArray();
        foreach (var rec in s.Records) rec.WriteTo(w);
        w.WriteEndArray();

        if (s.CurrentUrl is not null) w.WriteString("currentUrl", s.CurrentUrl);

        // ADR-0061: write LastOutcome — omit when it's the default None to
        // keep older snapshots' shape stable on round-trip (a snapshot with
        // None outcome reads identically whether the field is absent or
        // explicit). Saves a few bytes on the common case.
        if (s.LastOutcome is not AgentDecisionOutcome.None)
        {
            w.WritePropertyName("lastOutcome");
            AgentDecisionOutcomeCodec.Write(w, s.LastOutcome);
        }

        w.WriteEndObject();
    }

    public static AgentRunSnapshot Read(ref Utf8JsonReader r)
    {
        if (r.TokenType != JsonTokenType.StartObject) throw new JsonException("expected object");
        string? goal = null;
        int lastDecidedStep = 0;
        var history = new List<AgentDecision>();
        var visited = new List<string>();
        var records = new List<JsonObject>();
        string? currentUrl = null;
        AgentDecisionOutcome? lastOutcome = null;

        while (r.Read() && r.TokenType != JsonTokenType.EndObject)
        {
            var prop = r.GetString();
            r.Read();
            switch (prop)
            {
                case "goal": goal = r.GetString(); break;
                case "lastDecidedStep": lastDecidedStep = r.GetInt32(); break;
                case "currentUrl": currentUrl = r.GetString(); break;
                case "history":
                    if (r.TokenType != JsonTokenType.StartArray) throw new JsonException("history: expected array");
                    while (r.Read() && r.TokenType != JsonTokenType.EndArray)
                        history.Add(AgentDecisionCodec.Read(ref r));
                    break;
                case "visitedUrls":
                    if (r.TokenType != JsonTokenType.StartArray) throw new JsonException("visitedUrls: expected array");
                    while (r.Read() && r.TokenType != JsonTokenType.EndArray)
                        visited.Add(r.GetString() ?? throw new JsonException("visitedUrls: null entry"));
                    break;
                case "records":
                    if (r.TokenType != JsonTokenType.StartArray) throw new JsonException("records: expected array");
                    while (r.Read() && r.TokenType != JsonTokenType.EndArray)
                    {
                        var node = JsonNode.Parse(ref r);
                        records.Add(node as JsonObject
                            ?? throw new JsonException("records: entry was not a JSON object"));
                    }
                    break;
                case "lastOutcome":
                    lastOutcome = AgentDecisionOutcomeCodec.Read(ref r);
                    break;
                default: r.Skip(); break;
            }
        }

        return new AgentRunSnapshot(
            goal ?? throw new JsonException("missing required 'goal'"),
            lastDecidedStep,
            history,
            visited,
            records,
            currentUrl,
            // ADR-0061: missing lastOutcome on a pre-existing snapshot
            // deserialises to None (the constructor's default).
            lastOutcome);
    }
}

internal sealed class AgentRunSnapshotJsonConverter : JsonConverter<AgentRunSnapshot>
{
    public override AgentRunSnapshot Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o)
        => AgentRunSnapshotCodec.Read(ref reader);

    public override void Write(Utf8JsonWriter writer, AgentRunSnapshot value, JsonSerializerOptions o)
        => AgentRunSnapshotCodec.Write(writer, value);
}
