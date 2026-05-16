using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WebReaper.Sinks.Abstract;

namespace WebReaper.Sinks.Concrete;

/// <summary>
/// JSON Lines: no header, one compact JSON object per line. The
/// <c>Formatting.None</c> rendering is moved verbatim from the old
/// <c>JsonLinesFileSink</c> (ADR 0006) — the format quirk relocated, not
/// changed.
/// </summary>
public sealed class JsonLinesFormat : IFileSinkFormat
{
    public string? Header(JObject firstRow) => null;

    public string FormatRow(JObject row) => row.ToString(Formatting.None);
}
