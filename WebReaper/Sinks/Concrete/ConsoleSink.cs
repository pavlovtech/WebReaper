using Newtonsoft.Json.Linq;
using WebReaper.Sinks.Abstract;

namespace WebReaper.Sinks.Concrete;

public class ConsoleSink : IScraperSink
{
    public Task EmitAsync(JObject scrapedData, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"{scrapedData.ToString()}");
        return Task.CompletedTask;
    }
}