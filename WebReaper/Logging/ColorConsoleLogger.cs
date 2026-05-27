using Microsoft.Extensions.Logging;

namespace WebReaper.Logging;

internal sealed class ColorConsoleLogger : ILogger
{
    private Dictionary<LogLevel, ConsoleColor> LogLevelToColorMap { get; } = new()
    {
        [LogLevel.Trace] = ConsoleColor.DarkGray,
        [LogLevel.Debug] = ConsoleColor.Gray,
        [LogLevel.Information] = ConsoleColor.Green,
        [LogLevel.Warning] = ConsoleColor.Cyan,
        [LogLevel.Error] = ConsoleColor.Red,
        [LogLevel.Critical] = ConsoleColor.Red,
        [LogLevel.None] = ConsoleColor.Gray
    };

    // ILogger.BeginScope's interface signature is
    // `IDisposable? BeginScope<TState>(TState state) where TState : notnull`
    // — match it exactly to clear CS8633 (nullability + constraint
    // mismatch). The body keeps returning null (no real scoping).
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var originalColor = Console.ForegroundColor;

        Console.ForegroundColor = LogLevelToColorMap[logLevel];
        Console.WriteLine($"[{logLevel}] {formatter(state, exception)}");

        if (exception != null) Console.WriteLine($"{Environment.NewLine}{exception}");

        Console.ForegroundColor = originalColor;
    }
}