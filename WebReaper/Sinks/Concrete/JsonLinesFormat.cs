using System.Text.Json.Nodes;
using WebReaper.Sinks.Abstract;

namespace WebReaper.Sinks.Concrete;

/// <summary>
/// JSON Lines: no header, one compact JSON object per line. ADR 0008: the
/// compact rendering is now <see cref="JsonObject.ToJsonString"/> (no
/// Newtonsoft) — observable file content is unchanged (ADR 0006: the format
/// quirk relocated, not changed).
/// </summary>
public sealed class JsonLinesFormat : IFileSinkFormat
{
    public string? Header(JsonObject firstRow) => null;

    public string FormatRow(JsonObject row) => row.ToJsonString();
}
