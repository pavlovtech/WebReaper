using Newtonsoft.Json.Linq;

namespace WebReaper.Sinks.Abstract;

/// <summary>
/// The per-format quirk a file sink quarantines (ADR 0006): how one
/// <see cref="JObject"/> row becomes a line, and whether the file carries a
/// header derived from the first row. The buffered-drain mechanism
/// (<c>BufferedFileSink</c>) is shared; this seam is the only thing that
/// legitimately differs between a JSON-lines and a CSV file sink. Two real
/// adapters today (<c>JsonLinesFormat</c>, <c>CsvFormat</c>) — a real seam, not
/// indirection without variation.
/// </summary>
public interface IFileSinkFormat
{
    /// <summary>
    /// The header line for a file whose first row is <paramref name="firstRow"/>,
    /// or <c>null</c> if this format has no header. Called at most once, on the
    /// first drained row, before that row's data line. No trailing newline.
    /// </summary>
    string? Header(JObject firstRow);

    /// <summary>One data row → one line, without a trailing newline.</summary>
    string FormatRow(JObject row);
}
