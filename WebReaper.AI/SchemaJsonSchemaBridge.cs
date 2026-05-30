using System.Text.Json.Nodes;
using WebReaper.Domain.Parsing;

namespace WebReaper.AI;

/// <summary>
/// Pure function: WebReaper <see cref="Schema"/> → JSON Schema
/// (the LLM structured-output spec). Recursive; selectors are dropped
/// (LLMs do not extract by selector), only the field name, type, list
/// shape, and nesting are preserved. Used by
/// <see cref="LlmContentExtractor"/>.
/// </summary>
internal static class SchemaJsonSchemaBridge
{
    public static JsonObject ToJsonSchema(Schema schema)
    {
        var properties = new JsonObject();
        var required = new JsonArray();

        foreach (var child in schema.Children)
        {
            properties[child.Field!] = ToProperty(child);
            required.Add((JsonNode?)child.Field!);
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required
        };
    }

    private static JsonObject ToProperty(SchemaElement element)
    {
        // Nested container: a Schema whose Children > 0.
        if (element is Schema container)
        {
            if (container.IsList)
            {
                // Array of objects.
                return new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = ToJsonSchema(container)
                };
            }

            // Single nested object.
            return ToJsonSchema(container);
        }

        // Leaf element.
        if (element.IsList)
        {
            return new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject { ["type"] = ToJsonType(element.Type) }
            };
        }

        return new JsonObject { ["type"] = ToJsonType(element.Type) };
    }

    // WebReaper's DataType → JSON Schema type. Default is "string" for
    // an untyped leaf (matches the fold's untyped-pass-through.
    // ADR-0029's swallow-and-log policy means a wrong type at the
    // model layer logs and continues; here we err on permissive).
    private static string ToJsonType(DataType? type) => type switch
    {
        DataType.Integer => "integer",
        DataType.Float => "number",
        DataType.Boolean => "boolean",
        DataType.DataTime => "string",  // JSON Schema has no datetime; the
                                        // model returns an ISO string the
                                        // fold's Coerce parses.
        DataType.String => "string",
        _ => "string"
    };
}
