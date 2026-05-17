using System.Text.Json.Nodes;
using WebReaper.Sinks.Abstract;

namespace WebReaper.Sinks.Concrete;

/// <summary>
/// CSV: a header of the first row's flattened leaf names, then one
/// quoted, comma-joined line per row. ADR 0008: the same flatten/quote quirk
/// (ADR 0006), now over System.Text.Json.Nodes — no Newtonsoft. The leaf-name
/// is the tail of <see cref="JsonNode.GetPath"/> after the last <c>.</c>,
/// which reproduces the old Newtonsoft <c>JValue.Path</c> tail exactly
/// (<c>$.name</c>→<c>name</c>, <c>$.a.b</c>→<c>b</c>, <c>$.a[0]</c>→<c>a[0]</c>),
/// so observable file content is unchanged.
/// </summary>
public sealed class CsvFormat : IFileSinkFormat
{
    public string? Header(JsonObject firstRow)
    {
        var flattened = Leaves(firstRow)
            .Select(leaf =>
            {
                var path = leaf.GetPath();
                return path[(path.LastIndexOf('.') + 1)..];
            });

        return string.Join(",", flattened);
    }

    public string FormatRow(JsonObject row)
    {
        var flattened = Leaves(row)
            .Select(leaf => $"\"{leaf.ToString().Replace("\"", "\"\"")}\"");

        return string.Join(",", flattened);
    }

    // The System.Text.Json.Nodes equivalent of Newtonsoft's
    // Descendants().OfType<JValue>(): every scalar leaf, in document order
    // (JsonObject preserves insertion order, as JObject did).
    private static IEnumerable<JsonValue> Leaves(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var kv in obj)
                    foreach (var leaf in Leaves(kv.Value))
                        yield return leaf;
                break;
            case JsonArray arr:
                foreach (var element in arr)
                    foreach (var leaf in Leaves(element))
                        yield return leaf;
                break;
            case JsonValue value:
                yield return value;
                break;
        }
    }
}
