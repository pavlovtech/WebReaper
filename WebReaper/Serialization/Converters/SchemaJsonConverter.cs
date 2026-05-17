using System.Text.Json;
using System.Text.Json.Serialization;
using WebReaper.Domain.Parsing;

namespace WebReaper.Serialization.Converters;

/// <summary>
/// Polymorphic <see cref="Schema"/>/<see cref="SchemaElement"/> codec (ADR
/// 0008). <see cref="Schema"/> is the container node <em>and</em> an
/// <see cref="System.Collections.Generic.ICollection{T}"/>; without an
/// explicit converter STJ serialises it as a bare JSON array and drops
/// <c>Field</c>/<c>Children</c>. The codec keeps it an object and carries a
/// <c>$kind</c> discriminator — the <c>TypeNameHandling.Auto</c> replacement
/// for this hierarchy. Fully manual recursion, no nested serializer calls
/// (AOT-trivially-safe).
/// </summary>
internal static class SchemaCodec
{
    public static void Write(Utf8JsonWriter w, SchemaElement e)
    {
        w.WriteStartObject();
        var isContainer = e is Schema;
        w.WriteString("$kind", isContainer ? "container" : "leaf");
        if (e.Field is not null) w.WriteString("field", e.Field);
        if (e.Selector is not null) w.WriteString("selector", e.Selector);
        if (e.Attr is not null) w.WriteString("attr", e.Attr);
        if (e.Type is { } t) w.WriteString("type", t.ToString());
        if (e.GetHtml) w.WriteBoolean("getHtml", true);
        if (e.IsList) w.WriteBoolean("isList", true);
        if (isContainer)
        {
            var s = (Schema)e;
            w.WritePropertyName("children");
            w.WriteStartArray();
            foreach (var child in s.Children) Write(w, child);
            w.WriteEndArray();
        }
        w.WriteEndObject();
    }

    public static SchemaElement Read(ref Utf8JsonReader r)
    {
        if (r.TokenType != JsonTokenType.StartObject) throw new JsonException("expected object");
        string? kind = null, field = null, selector = null, attr = null, typeName = null;
        bool getHtml = false, isList = false;
        List<SchemaElement>? children = null;

        while (r.Read() && r.TokenType != JsonTokenType.EndObject)
        {
            var prop = r.GetString();
            r.Read();
            switch (prop)
            {
                case "$kind": kind = r.GetString(); break;
                case "field": field = r.GetString(); break;
                case "selector": selector = r.GetString(); break;
                case "attr": attr = r.GetString(); break;
                case "type": typeName = r.GetString(); break;
                case "getHtml": getHtml = r.GetBoolean(); break;
                case "isList": isList = r.GetBoolean(); break;
                case "children":
                    children = new List<SchemaElement>();
                    while (r.Read() && r.TokenType != JsonTokenType.EndArray)
                        children.Add(Read(ref r));
                    break;
                default: r.Skip(); break;
            }
        }

        DataType? type = typeName switch
        {
            null => null,
            "None" => DataType.None,
            "Integer" => DataType.Integer,
            "Float" => DataType.Float,
            "Boolean" => DataType.Boolean,
            "String" => DataType.String,
            "DataTime" => DataType.DataTime,
            "Object" => DataType.Object,
            _ => throw new JsonException($"unknown DataType '{typeName}'")
        };

        if (kind == "container")
        {
            var s = new Schema(field);
            if (children is not null) s.Children = children;
            s.Selector = selector; s.Attr = attr; s.Type = type;
            s.GetHtml = getHtml; s.IsList = isList;
            return s;
        }

        return new SchemaElement
        {
            Field = field, Selector = selector, Attr = attr,
            Type = type, GetHtml = getHtml, IsList = isList
        };
    }
}

internal sealed class SchemaJsonConverter : JsonConverter<Schema>
{
    public override Schema? Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o)
        => reader.TokenType == JsonTokenType.Null ? null : (Schema)SchemaCodec.Read(ref reader);

    public override void Write(Utf8JsonWriter writer, Schema value, JsonSerializerOptions o)
        => SchemaCodec.Write(writer, value);
}

internal sealed class SchemaElementJsonConverter : JsonConverter<SchemaElement>
{
    public override SchemaElement? Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o)
        => reader.TokenType == JsonTokenType.Null ? null : SchemaCodec.Read(ref reader);

    public override void Write(Utf8JsonWriter writer, SchemaElement value, JsonSerializerOptions o)
        => SchemaCodec.Write(writer, value);
}
