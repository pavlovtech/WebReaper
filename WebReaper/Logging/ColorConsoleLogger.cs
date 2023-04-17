using Microsoft.Extensions.Logging;

namespace WebReaper.Logging;

public sealed class ColorConsoleLogger : ILogger
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

    public IDisposable BeginScope<TState>(TState state)
    {
        return default!;
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