using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace WebReaper.Extensions;

public static class LoggerExtensions
{
    public static IDisposable Measure(
        this ILogger logger,
        [CallerMemberName] string callerName = "",
        [CallerFilePathAttribute] string filePath = "",
        [CallerLineNumber] int sourceLineNumber = 0) 
    {
        return new AutoTracker(logger, callerName, filePath, sourceLineNumber);
    }
}

public class AutoTracker : IDisposable
{
    private string callerName = "";
    private string filePath = "";
    private readonly int sourceLineNumber;
    private ILogger _logger;

    private static ConcurrentDictionary<string, int> _methodCounters = new();
    private static ConcurrentDictionary<string, long> _methodTotalDuration = new();

    Stopwatch watch = new Stopwatch();

    public AutoTracker(
        ILogger logger,
        [CallerMemberName] string callerName = "",
        [CallerFilePathAttribute] string filePath = "",
        [CallerLineNumber] int sourceLineNumber = 0)
    {
        _logger = logger;

        _methodCounters.AddOrUpdate(callerName,
            (key) => 1, (key, value) => value + 1);

        _logger.LogInformation("Started executing {method}.", callerName);

        watch.Start();

        this.callerName = callerName;
        this.filePath = filePath;
        this.sourceLineNumber = sourceLineNumber;
    }

    public void Dispose()
    {
        watch.Stop();

        _methodTotalDuration.AddOrUpdate(callerName,
           (key) => watch.ElapsedMilliseconds,
           (key, value) => value + watch.ElapsedMilliseconds);

        _logger.LogInformation("Finished executing {method}. Duration: {elapsed} ms. Total duration of all invocations: {total}. Total count of invocations: {invocationsCount}",
                callerName,
                watch.ElapsedMilliseconds,
                _methodTotalDuration[callerName],
                _methodCounters[callerName]);
    }
}