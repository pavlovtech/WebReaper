using Newtonsoft.Json.Linq;
using WebReaper.Abstractions.Sinks;

namespace WebReaper.Core.Sinks;

public class ConsoleSink : IScraperSink
{
    public Task EmitAsync(JObject scrapedData)
    {
        Console.WriteLine($"{scrapedData.ToString()}");
        return Task.CompletedTask;
    }
}