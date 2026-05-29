using System.Text.Json;
using System.Text.Json.Nodes;
using WebReaper.Domain.Parsing;

namespace WebReaper.Cli;

// Shared schema-file loader for `scrape` and `crawl` (ADR-0043 / ADR-0081). The
// JSON shape is the ADR-0043 contract:
//   { field, selector?, type?, attr?, is_list?, children? }
// An object with children is a Schema (a nested container); a leaf with no
// children is a SchemaElement. AOT-clean: JsonNode parsing, no reflection.
internal static class SchemaFile
{
    public static Schema Load(string path)
    {
        if (!File.Exists(path))
            throw new CliException($"Schema file not found: {path}");

        string content;
        try { content = File.ReadAllText(path); }
        catch (Exception ex)
        {
            throw new CliException($"Failed to read schema file '{path}': {ex.Message}");
        }

        JsonNode? root;
        try { root = JsonNode.Parse(content); }
        catch (JsonException ex)
        {
            throw new CliException($"Schema file '{path}' is not valid JSON: {ex.Message}");
        }

        if (root is not JsonObject obj)
            throw new CliException(
                $"Schema file '{path}' must contain a JSON object at the root.");

        return BuildSchema(obj);
    }

    private static Schema BuildSchema(JsonObject obj)
    {
        var children = obj["children"] as JsonArray;

        if (children is null || children.Count == 0)
        {
            // Leaf.
            return WrapAsSchema(BuildElement(obj));
        }

        // Container.
        var field = obj["field"]?.GetValue<string>();
        var selector = obj["selector"]?.GetValue<string>();
        var isList = obj["is_list"]?.GetValue<bool>() ?? false;

        var container = field is not null
            ? new Schema(field) { Selector = selector ?? string.Empty, IsList = isList }
            : new Schema();

        foreach (var child in children)
        {
            if (child is not JsonObject childObj)
                throw new CliException("Schema children must be objects.");
            container.Add(BuildElement(childObj));
        }

        return container;
    }

    private static SchemaElement BuildElement(JsonObject obj)
    {
        var field = obj["field"]?.GetValue<string>()
            ?? throw new CliException("Schema element is missing 'field'.");

        var children = obj["children"] as JsonArray;
        if (children is not null && children.Count > 0)
        {
            return BuildSchema(obj);
        }

        var selector = obj["selector"]?.GetValue<string>() ?? string.Empty;
        var attr = obj["attr"]?.GetValue<string>();
        var isList = obj["is_list"]?.GetValue<bool>() ?? false;
        var type = ParseDataType(obj["type"]?.GetValue<string>());

        var element = new SchemaElement(field, selector)
        {
            Type = type,
            IsList = isList
        };

        if (attr is not null) element.Attr = attr;

        return element;
    }

    private static Schema WrapAsSchema(SchemaElement element)
    {
        if (element is Schema schema) return schema;
        return new Schema { element };
    }

    private static DataType? ParseDataType(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        return raw.ToLowerInvariant() switch
        {
            "string" => DataType.String,
            "integer" or "int" => DataType.Integer,
            "float" or "double" or "decimal" => DataType.Float,
            "boolean" or "bool" => DataType.Boolean,
            "datetime" or "date" => DataType.DataTime,
            _ => throw new CliException(
                $"Unknown schema type '{raw}'. Valid: string, integer, float, boolean, datetime.")
        };
    }
}
