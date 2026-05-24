using System.Text.Json;
using System.Text.Json.Serialization;
using WebReaper.Domain.Agent;
using WebReaper.Domain.PageActions;

namespace WebReaper.Serialization.Converters;

/// <summary>
/// <see cref="AgentDecision"/> codec (ADR-0051). Same shape as
/// <see cref="PageActionCodec"/>: a <c>type</c> discriminator plus the arm's
/// typed fields, with <c>reason</c> on every arm. Recursively uses
/// <see cref="SchemaCodec"/> for the Extract arm's Schema and
/// <see cref="PageActionCodec"/> for the Act arm's nested action — fully
/// manual recursion, no nested serializer calls (AOT-trivially-safe).
/// </summary>
internal static class AgentDecisionCodec
{
    public static void Write(Utf8JsonWriter w, AgentDecision d)
    {
        w.WriteStartObject();
        w.WriteString("reason", d.Reason);
        switch (d)
        {
            case AgentDecision.Extract x:
                w.WriteString("type", "extract");
                w.WritePropertyName("schema");
                SchemaCodec.Write(w, x.Schema);
                break;
            case AgentDecision.Follow x:
                w.WriteString("type", "follow");
                w.WriteString("url", x.Url);
                break;
            case AgentDecision.Act x:
                w.WriteString("type", "act");
                w.WritePropertyName("action");
                PageActionCodec.Write(w, x.Action);
                break;
            case AgentDecision.Stop:
                w.WriteString("type", "stop");
                break;
            default:
                throw new JsonException($"unhandled AgentDecision arm '{d.GetType().Name}'");
        }
        w.WriteEndObject();
    }

    public static AgentDecision Read(ref Utf8JsonReader r)
    {
        if (r.TokenType != JsonTokenType.StartObject) throw new JsonException("expected object");
        string? type = null;
        string? reason = null;
        string? url = null;
        WebReaper.Domain.Parsing.Schema? schema = null;
        PageAction? action = null;
        while (r.Read() && r.TokenType != JsonTokenType.EndObject)
        {
            var prop = r.GetString();
            r.Read();
            switch (prop)
            {
                case "type": type = r.GetString(); break;
                case "reason": reason = r.GetString(); break;
                case "url": url = r.GetString(); break;
                case "schema":
                    var schemaElement = SchemaCodec.Read(ref r);
                    schema = schemaElement as WebReaper.Domain.Parsing.Schema
                        ?? throw new JsonException("AgentDecision.Extract schema must be a Schema container, not a leaf SchemaElement");
                    break;
                case "action": action = PageActionCodec.Read(ref r); break;
                default: r.Skip(); break;
            }
        }
        reason ??= "";
        return type switch
        {
            "extract" => new AgentDecision.Extract(Require(schema, "schema", type)) { Reason = reason },
            "follow" => new AgentDecision.Follow(Require(url, "url", type)) { Reason = reason },
            "act" => new AgentDecision.Act(Require(action, "action", type)) { Reason = reason },
            "stop" => new AgentDecision.Stop { Reason = reason },
            _ => throw new JsonException($"unknown AgentDecision type '{type}'")
        };
    }

    private static T Require<T>(T? value, string field, string? type) where T : class =>
        value ?? throw new JsonException($"AgentDecision '{type}' is missing required '{field}'");
}

internal sealed class AgentDecisionJsonConverter : JsonConverter<AgentDecision>
{
    public override AgentDecision Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o)
        => AgentDecisionCodec.Read(ref reader);

    public override void Write(Utf8JsonWriter writer, AgentDecision value, JsonSerializerOptions o)
        => AgentDecisionCodec.Write(writer, value);
}
