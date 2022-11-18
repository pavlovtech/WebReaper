using Exoscan.Sinks.Abstract;
using Exoscan.Sinks.Models;
using Newtonsoft.Json.Linq;

namespace Exoscan.Sinks.Concrete;

public class ConsoleSink : IScraperSink
{
    public Task EmitAsync(ParsedData entity, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"{entity.Data}");
        return Task.CompletedTask;
    }
}