namespace WebReaper.Sinks.Concrete;

/// <summary>
/// CSV file sink — a header from the first row, then quoted, comma-joined
/// rows. A thin <see cref="BufferedFileSink"/> over <see cref="CsvFormat"/>;
/// the buffered-drain mechanism has exactly one home (ADR 0006). The public
/// ctor is preserved, so the builder and consumer code are unchanged
/// (source-compatible, minor SemVer).
/// </summary>
public class CsvFileSink : BufferedFileSink
{
    public CsvFileSink(string filePath, bool dataCleanupOnStart)
        : base(filePath, dataCleanupOnStart, new CsvFormat())
    {
    }
}
