using Microsoft.Extensions.Logging;

namespace WebReaper.ConsoleApplication
{

    public sealed class ColorConsoleLogger : ILogger
    {
        public Dictionary<LogLevel, ConsoleColor> LogLevelToColorMap { get; set; } = new()
        {
            [LogLevel.Information] = ConsoleColor.Green,
            [LogLevel.Warning] = ConsoleColor.Cyan,
            [LogLevel.Error] = ConsoleColor.Red,
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

            ConsoleColor originalColor = Console.ForegroundColor;

            Console.ForegroundColor = LogLevelToColorMap[logLevel];
            Console.Write($"[ {logLevel} ] ");

            Console.ForegroundColor = LogLevelToColorMap[logLevel];
            Console.Write($"{formatter(state, exception)}");

            Console.ForegroundColor = originalColor;
            Console.WriteLine();
        }
    }
}
