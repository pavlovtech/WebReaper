using System.Text.Json;
using System.Text.Json.Serialization;
using WebReaper.Domain.PageActions;

namespace WebReaper.Serialization.Converters;

/// <summary>
/// <see cref="PageAction"/> codec (ADR 0008). <c>Parameters</c> is a
/// genuinely-polymorphic <c>object[]</c>; STJ's default rematerialises a
/// <see cref="JsonElement"/>, on which <c>Convert.ToInt32</c> throws — the
/// exact ADR-0003/0005 "polymorphic member loses its type across a serializer
/// boundary" defect. Each element is written <em>kind-tagged</em> and read
/// back to its CLR type, so the round-trip keeps type fidelity.
/// </summary>
internal static class PageActionCodec
{
    public static void Write(Utf8JsonWriter w, PageAction a)
    {
        w.WriteStartObject();
        w.WriteString("type", a.Type.ToString());
        w.WritePropertyName("parameters");
        w.WriteStartArray();
        foreach (var p in a.Parameters)
        {
            w.WriteStartObject();
            switch (p)
            {
                case null: w.WriteString("k", "n"); break;
                case string s: w.WriteString("k", "s"); w.WriteString("v", s); break;
                case bool b: w.WriteString("k", "b"); w.WriteBoolean("v", b); break;
                case sbyte or byte or short or ushort or int or uint or long:
                    w.WriteString("k", "i"); w.WriteNumber("v", Convert.ToInt64(p)); break;
                case ulong ul: w.WriteString("k", "i"); w.WriteNumber("v", ul); break;
                case float or double or decimal:
                    w.WriteString("k", "d"); w.WriteNumber("v", Convert.ToDouble(p)); break;
                default: w.WriteString("k", "s"); w.WriteString("v", p.ToString()); break;
            }
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteEndObject();
    }

    public static PageAction Read(ref Utf8JsonReader r)
    {
        if (r.TokenType != JsonTokenType.StartObject) throw new JsonException("expected object");
        PageActionType type = default;
        var ps = new List<object>();
        while (r.Read() && r.TokenType != JsonTokenType.EndObject)
        {
            var prop = r.GetString();
            r.Read();
            if (prop == "type")
            {
                type = r.GetString() switch
                {
                    "Click" => PageActionType.Click,
                    "Wait" => PageActionType.Wait,
                    "ScrollToEnd" => PageActionType.ScrollToEnd,
                    "EvaluateExpression" => PageActionType.EvaluateExpression,
                    "WaitForSelector" => PageActionType.WaitForSelector,
                    "WaitForNetworkIdle" => PageActionType.WaitForNetworkIdle,
                    var x => throw new JsonException($"unknown PageActionType '{x}'")
                };
            }
            else if (prop == "parameters")
            {
                while (r.Read() && r.TokenType != JsonTokenType.EndArray)
                {
                    string? kind = null; object? val = null;
                    while (r.Read() && r.TokenType != JsonTokenType.EndObject)
                    {
                        var pn = r.GetString();
                        r.Read();
                        if (pn == "k") kind = r.GetString();
                        else if (pn == "v")
                            val = kind switch
                            {
                                "s" => r.GetString(),
                                "b" => r.GetBoolean(),
                                "i" => r.TryGetInt32(out var i) ? i : r.GetInt64(),
                                "d" => r.GetDouble(),
                                _ => r.GetString()
                            };
                    }
                    ps.Add(kind == "n" ? null! : val!);
                }
            }
            else r.Skip();
        }
        return new PageAction(type, ps.ToArray());
    }
}

internal sealed class PageActionJsonConverter : JsonConverter<PageAction>
{
    public override PageAction Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o)
        => PageActionCodec.Read(ref reader);

    public override void Write(Utf8JsonWriter writer, PageAction value, JsonSerializerOptions o)
        => PageActionCodec.Write(writer, value);
}
