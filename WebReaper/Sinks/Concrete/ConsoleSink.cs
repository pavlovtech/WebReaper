using WebReaper.Sinks.Abstract;
using WebReaper.Sinks.Models;

namespace WebReaper.Sinks.Concrete;

internal class ConsoleSink : IScraperSink
{
    public bool DataCleanupOnStart { get; set; }

    public Task EmitAsync(ParsedData entity, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"{entity.Data}");
        return Task.CompletedTask;
    }
}