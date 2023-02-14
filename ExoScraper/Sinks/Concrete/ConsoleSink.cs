using ExoScraper.Sinks.Abstract;
using ExoScraper.Sinks.Models;
using Newtonsoft.Json.Linq;

namespace ExoScraper.Sinks.Concrete;

public class ConsoleSink : IScraperSink
{
    public Task EmitAsync(ParsedData entity, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"{entity.Data}");
        return Task.CompletedTask;
    }
}