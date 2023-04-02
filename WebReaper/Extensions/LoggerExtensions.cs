using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace WebReaper.Extensions;

public static class LoggerExtensions
{
    public static IDisposable LogMethodDuration(
        this ILogger logger,
        [CallerMemberName] string callerName = "")
    {
        return new Timer(logger, callerName);
    }

    public static void LogInvocationCount(
        this ILogger logger,
        [CallerMemberName] string callerName = "")
    {
        Counter.LogCount(logger, callerName);
    }
}

public class Timer : IDisposable
{
    private readonly ILogger _logger;
    private readonly string callerName = "";


    private readonly Stopwatch watch = new();

    public Timer(
        ILogger logger,
        [CallerMemberName] string callerName = "")
    {
        _logger = logger;

        watch.Start();

        this.callerName = callerName;
    }

    public void Dispose()
    {
        watch.Stop();

        _logger.LogInformation("{method} finished in {elapsed} ms",
            callerName,
            watch.ElapsedMilliseconds);
    }
}

public static class Counter
{
    private static readonly ConcurrentDictionary<string, int> _methodCounters = new();

    public static void LogCount(
        ILogger logger,
        [CallerMemberName] string callerName = "")
    {
        _methodCounters.AddOrUpdate(callerName,
            key => 1, (key, value) => value + 1);

        logger.LogInformation("{method} was called {invocationsCount} times",
            callerName,
            _methodCounters[callerName]);
    }
}