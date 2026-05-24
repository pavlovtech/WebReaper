using System.Text.Json.Nodes;
using WebReaper.Domain.Parsing;

namespace WebReaper.Core.Parser.Concrete;

/// <summary>
/// The default <see cref="ExtractionRouter"/> validator (ADR-0046): a
/// recursive walk over the <see cref="Schema"/> testing each declared
/// element against the extractor's <see cref="JsonObject"/> output.
/// Reports the result <em>unsatisfied</em> — and routes to the
/// fallback — when any required leaf is empty or absent.
/// </summary>
public static class SchemaSatisfiedValidator
{
    /// <summary>
    /// Returns <c>true</c> when <paramref name="result"/> satisfies
    /// every required element of <paramref name="schema"/>. The
    /// per-element check:
    /// <list type="bullet">
    /// <item>Leaf, non-list: present <em>and</em> (if string-typed) non-empty.</item>
    /// <item>Leaf, IsList: present <em>and</em> non-empty array.</item>
    /// <item>Container (Schema), non-list: present.</item>
    /// <item>Container (Schema), IsList: present and non-empty array.</item>
    /// </list>
    /// A null <paramref name="schema"/> is trivially satisfied — no
    /// elements to check.
    /// </summary>
    public static bool IsSatisfied(JsonObject result, Schema? schema)
    {
        if (schema is null) return true;

        foreach (var child in schema.Children)
        {
            if (!IsElementSatisfied(result, child)) return false;
        }
        return true;
    }

    private static bool IsElementSatisfied(JsonObject parent, SchemaElement element)
    {
        var field = element.Field;
        if (field is null) return true;

        if (!parent.TryGetPropertyValue(field, out var node) || node is null)
            return false;

        // Containers — including IsList containers (a list of objects).
        if (element is Schema container)
        {
            if (container.IsList)
            {
                return node is JsonArray array && array.Count > 0;
            }
            return true;
        }

        // Leaf, IsList = true.
        if (element.IsList)
        {
            return node is JsonArray arr && arr.Count > 0;
        }

        // Leaf, non-list. A string-typed leaf with empty value counts as
        // missing — the fold writes empty string when the selector matched
        // nothing (ADR-0029). Other types: presence is sufficient — a
        // legitimate "0" or "false" is valid data.
        if (node is JsonValue value)
        {
            if (element.Type is null || element.Type == DataType.String)
            {
                var s = value.TryGetValue<string>(out var str) ? str : null;
                return !string.IsNullOrEmpty(s);
            }
            return true;
        }

        return true;
    }
}
