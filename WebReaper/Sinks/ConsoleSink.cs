using Newtonsoft.Json.Linq;
using WebReaper.Absctracts.Sinks;

namespace WebReaper.Sinks;

public class ConsoleSink : IScraperSink
{
    public bool IsInitialized => true;

    public Task EmitAsync(JObject scrapedData) 
    {
        Console.WriteLine($"{scrapedData.ToString()}");
        return Task.CompletedTask;
    }

    public Task InitAsync() => Task.CompletedTask;
}