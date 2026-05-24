using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using WebReaper.Domain.Agent;
using WebReaper.Domain.PageActions;

namespace WebReaper.Serialization.Converters;

/// <summary>
/// <see cref="AgentDecisionOutcome"/> codec (ADR-0061). Same shape as
/// <see cref="AgentDecisionCodec"/>: a <c>type</c> discriminator plus the
/// arm's typed fields. Recursively uses
/// <see cref="JsonNode.Parse(ref Utf8JsonReader, JsonNodeOptions?)"/> /
/// <see cref="JsonObject.WriteTo(Utf8JsonWriter, JsonSerializerOptions)"/> for
/// the <c>Extracted.Record</c> payload and <see cref="PageActionCodec"/> for
/// the <c>ActDispatched.ResolvedAction</c> — fully manual recursion, no
/// nested serializer calls (AOT-trivially-safe).
/// </summary>
internal static class AgentDecisionOutcomeCodec
{
    public static void Write(Utf8JsonWriter w, AgentDecisionOutcome o)
    {
        w.WriteStartObject();
        switch (o)
        {
            case AgentDecisionOutcome.None:
                w.WriteString("type", "none");
                break;
            case AgentDecisionOutcome.Extracted x:
                w.WriteString("type", "extracted");
                w.WriteNumber("recordCount", x.RecordCount);
                if (x.Record is not null)
                {
                    w.WritePropertyName("record");
                    x.Record.WriteTo(w);
                }
                break;
            case AgentDecisionOutcome.Followed x:
                w.WriteString("type", "followed");
                w.WriteString("actualUrl", x.ActualUrl);
                w.WriteNumber("statusCode", x.StatusCode);
                break;
            case AgentDecisionOutcome.ActDispatched x:
                w.WriteString("type", "actDispatched");
                w.WritePropertyName("resolvedAction");
                PageActionCodec.Write(w, x.ResolvedAction);
                break;
            case AgentDecisionOutcome.Failed x:
                w.WriteString("type", "failed");
                w.WriteString("reason", x.Reason);
                if (x.ExceptionType is not null)
                    w.WriteString("exceptionType", x.ExceptionType);
                break;
            case AgentDecisionOutcome.Stopped x:
                w.WriteString("type", "stopped");
                w.WriteString("reason", x.Reason);
                break;
            default:
                throw new JsonException($"unhandled AgentDecisionOutcome arm '{o.GetType().Name}'");
        }
        w.WriteEndObject();
    }

    public static AgentDecisionOutcome Read(ref Utf8JsonReader r)
    {
        if (r.TokenType != JsonTokenType.StartObject) throw new JsonException("expected object");
        string? type = null;
        string? reason = null;
        string? exceptionType = null;
        string? actualUrl = null;
        int statusCode = 0;
        int recordCount = 0;
        JsonObject? record = null;
        PageAction? resolvedAction = null;

        while (r.Read() && r.TokenType != JsonTokenType.EndObject)
        {
            var prop = r.GetString();
            r.Read();
            switch (prop)
            {
                case "type": type = r.GetString(); break;
                case "reason": reason = r.GetString(); break;
                case "exceptionType": exceptionType = r.GetString(); break;
                case "actualUrl": actualUrl = r.GetString(); break;
                case "statusCode": statusCode = r.GetInt32(); break;
                case "recordCount": recordCount = r.GetInt32(); break;
                case "record":
                    if (r.TokenType == JsonTokenType.Null) { record = null; break; }
                    var node = JsonNode.Parse(ref r);
                    record = node as JsonObject
                        ?? throw new JsonException("AgentDecisionOutcome.Extracted record was not a JSON object");
                    break;
                case "resolvedAction":
                    resolvedAction = PageActionCodec.Read(ref r);
                    break;
                default: r.Skip(); break;
            }
        }

        return type switch
        {
            "none" => new AgentDecisionOutcome.None(),
            "extracted" => new AgentDecisionOutcome.Extracted(record, recordCount),
            "followed" => new AgentDecisionOutcome.Followed(
                Require(actualUrl, "actualUrl", type), statusCode),
            "actDispatched" => new AgentDecisionOutcome.ActDispatched(
                resolvedAction
                    ?? throw new JsonException("AgentDecisionOutcome.ActDispatched missing 'resolvedAction'")),
            "failed" => new AgentDecisionOutcome.Failed(
                Require(reason, "reason", type), exceptionType),
            "stopped" => new AgentDecisionOutcome.Stopped(
                Require(reason, "reason", type)),
            _ => throw new JsonException($"unknown AgentDecisionOutcome type '{type}'")
        };
    }

    private static string Require(string? value, string field, string? type) =>
        value ?? throw new JsonException($"AgentDecisionOutcome '{type}' is missing required '{field}'");
}

internal sealed class AgentDecisionOutcomeJsonConverter : JsonConverter<AgentDecisionOutcome>
{
    public override AgentDecisionOutcome Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o)
        => AgentDecisionOutcomeCodec.Read(ref reader);

    public override void Write(Utf8JsonWriter writer, AgentDecisionOutcome value, JsonSerializerOptions o)
        => AgentDecisionOutcomeCodec.Write(writer, value);
}
