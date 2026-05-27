using System.Text.Json;
using System.Text.Json.Serialization;
using WebReaper.Domain.PageActions;

namespace WebReaper.Serialization.Converters;

/// <summary>
/// <see cref="PageAction"/> codec (ADR 0008, ADR-0035). Since ADR-0035
/// <c>PageAction</c> is a closed sum of typed arms, not a
/// <c>(PageActionType, object[])</c> pair — so the codec is a <c>type</c>
/// discriminator plus the arm's typed fields, and the pre-0035 per-value
/// kind-tagging (the workaround for a genuinely-polymorphic <c>object[]</c>)
/// is gone.
/// </summary>
internal static class PageActionCodec
{
    public static void Write(Utf8JsonWriter w, PageAction a)
    {
        w.WriteStartObject();
        switch (a)
        {
            case PageAction.Click x:
                w.WriteString("type", "click");
                w.WriteString("selector", x.Selector);
                break;
            case PageAction.Wait x:
                w.WriteString("type", "wait");
                w.WriteNumber("ms", x.Milliseconds);
                break;
            case PageAction.ScrollToEnd:
                w.WriteString("type", "scrollToEnd");
                break;
            case PageAction.EvaluateExpression x:
                w.WriteString("type", "evaluateExpression");
                w.WriteString("expression", x.Expression);
                break;
            case PageAction.WaitForSelector x:
                w.WriteString("type", "waitForSelector");
                w.WriteString("selector", x.Selector);
                w.WriteNumber("timeoutMs", x.TimeoutMs);
                break;
            case PageAction.WaitForNetworkIdle:
                w.WriteString("type", "waitForNetworkIdle");
                break;
            case PageAction.SemanticAct x:
                // ADR-0050: persisted as the intent string only — the
                // resolved arm is a per-crawl runtime concern and is
                // intentionally never persisted (it'd freeze the LLM's
                // selector across crawls, defeating the resolve-on-cache-miss
                // recovery path).
                w.WriteString("type", "semanticAct");
                w.WriteString("intent", x.Intent);
                break;
            case PageAction.Press x:
                // ADR-0074: key string is the only field; no selector.
                w.WriteString("type", "press");
                w.WriteString("key", x.Key);
                break;
            default:
                throw new JsonException($"unhandled PageAction arm '{a.GetType().Name}'");
        }
        w.WriteEndObject();
    }

    public static PageAction Read(ref Utf8JsonReader r)
    {
        if (r.TokenType != JsonTokenType.StartObject) throw new JsonException("expected object");
        string? type = null, selector = null, expression = null, intent = null, key = null;
        int ms = 0, timeoutMs = 0;
        while (r.Read() && r.TokenType != JsonTokenType.EndObject)
        {
            var prop = r.GetString();
            r.Read();
            switch (prop)
            {
                case "type": type = r.GetString(); break;
                case "selector": selector = r.GetString(); break;
                case "expression": expression = r.GetString(); break;
                case "intent": intent = r.GetString(); break;
                case "key": key = r.GetString(); break;
                case "ms": ms = r.GetInt32(); break;
                case "timeoutMs": timeoutMs = r.GetInt32(); break;
                default: r.Skip(); break;
            }
        }
        return type switch
        {
            "click" => new PageAction.Click(Require(selector, "selector", type)),
            "wait" => new PageAction.Wait(ms),
            "scrollToEnd" => new PageAction.ScrollToEnd(),
            "evaluateExpression" => new PageAction.EvaluateExpression(Require(expression, "expression", type)),
            "waitForSelector" => new PageAction.WaitForSelector(Require(selector, "selector", type), timeoutMs),
            "waitForNetworkIdle" => new PageAction.WaitForNetworkIdle(),
            "semanticAct" => new PageAction.SemanticAct(Require(intent, "intent", type)),
            "press" => new PageAction.Press(Require(key, "key", type)),
            _ => throw new JsonException($"unknown PageAction type '{type}'")
        };
    }

    private static string Require(string? value, string field, string? type) =>
        value ?? throw new JsonException($"PageAction '{type}' is missing required '{field}'");
}

internal sealed class PageActionJsonConverter : JsonConverter<PageAction>
{
    public override PageAction Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o)
        => PageActionCodec.Read(ref reader);

    public override void Write(Utf8JsonWriter writer, PageAction value, JsonSerializerOptions o)
        => PageActionCodec.Write(writer, value);
}
