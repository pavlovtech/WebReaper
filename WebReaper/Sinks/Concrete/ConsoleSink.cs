using Newtonsoft.Json.Linq;
using WebReaper.Sinks.Abstract;
using WebReaper.Sinks.Models;

namespace WebReaper.Sinks.Concrete;

public class ConsoleSink : IScraperSink
{
    public Task EmitAsync(ParsedData data, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"{data.Data}");
        return Task.CompletedTask;
    }
}