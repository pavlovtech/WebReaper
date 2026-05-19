using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace WebReaper.Extensions;

/// <summary>
/// Diagnostic <see cref="ILogger"/> extensions used across WebReaper and the
/// satellites (e.g. the WebReaper.Puppeteer transport times its page loads
/// with <see cref="LogMethodDuration"/>).
/// </summary>
public static class LoggerExtensions
{
    /// <summary>
    /// Time the calling method: returns an <see cref="IDisposable"/> that, when
    /// disposed, logs the elapsed milliseconds. Use with
    /// <c>using var _ = logger.LogMethodDuration();</c>.
    /// </summary>
    public static IDisposable LogMethodDuration(
        this ILogger logger,
        [CallerMemberName] string callerName = "")
    {
        return new Timer(logger, callerName);
    }

    /// <summary>
    /// Log how many times the calling method has been invoked (a process-wide
    /// counter keyed by caller name) — a lightweight call-frequency probe.
    /// </summary>
    public static void LogInvocationCount(
        this ILogger logger,
        [CallerMemberName] string callerName = "")
    {
        Counter.LogCount(logger, callerName);
    }
}

internal class Timer : IDisposable
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

internal static class Counter
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