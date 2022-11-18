using Microsoft.Extensions.Logging;

namespace Exoscan.Logging;

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

    public IDisposable BeginScope<TState>(TState state) => default!;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var originalColor = Console.ForegroundColor;

        Console.ForegroundColor = LogLevelToColorMap[logLevel];
        Console.Write($"[ {logLevel} ] ");

        Console.ForegroundColor = LogLevelToColorMap[logLevel];
        Console.Write($"{formatter(state, exception)}");

        if (exception != null)
        {
            Console.WriteLine($"\n\n{exception}");
        }

        Console.ForegroundColor = originalColor;
        Console.WriteLine();
    }
}