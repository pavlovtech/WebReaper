using Newtonsoft.Json.Linq;
using WebReaper.Sinks.Abstract;

namespace WebReaper.Sinks.Concrete;

/// <summary>
/// CSV: a header of the first row's flattened leaf names, then one
/// quoted, comma-joined line per row. The flatten/quote expressions are moved
/// verbatim from the old <c>CsvFileSink</c> (ADR 0006) — the genuine CSV quirk
/// relocated, not changed.
/// </summary>
public sealed class CsvFormat : IFileSinkFormat
{
    public string? Header(JObject firstRow)
    {
        var flattened = firstRow
            .Descendants()
            .OfType<JValue>()
            .Select(jv => jv.Path.Remove(0, jv.Path.LastIndexOf(".", StringComparison.Ordinal) + 1));

        return string.Join(",", flattened);
    }

    public string FormatRow(JObject row)
    {
        var flattened = row
            .Descendants()
            .OfType<JValue>()
            .Select(p => $"\"{p.Value?.ToString()?.Replace("\"", "\"\"")}\"");

        return string.Join(",", flattened);
    }
}
