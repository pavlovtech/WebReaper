namespace WebReaper.Sinks.Concrete;

/// <summary>
/// JSON Lines file sink — one compact JSON object per line. A thin
/// <see cref="BufferedFileSink"/> over <see cref="JsonLinesFormat"/>; the
/// buffered-drain mechanism has exactly one home (ADR 0006). The public ctor
/// is preserved, so the builder and consumer code are unchanged
/// (source-compatible, minor SemVer).
/// </summary>
internal class JsonLinesFileSink : BufferedFileSink
{
    public JsonLinesFileSink(string filePath, bool dataCleanupOnStart)
        : base(filePath, dataCleanupOnStart, new JsonLinesFormat())
    {
    }
}
