using Microsoft.Extensions.Logging;

namespace WebReaper.UnitTests;

// ADR-0029: a small in-test ILogger that captures each Log call as a
// (LogLevel, message, exception) tuple. Used to pin the Schema fold's
// per-leaf swallow-and-log policy — the differentiated coercion-failure
// log messages become a tested contract, not a code comment.
internal sealed class CapturingLogger : ILogger
{
    public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; }
        = new();

    public IDisposable BeginScope<TState>(TState state) => default!;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Entries.Add((logLevel, formatter(state, exception), exception));
    }
}
