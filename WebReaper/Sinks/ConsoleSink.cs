using Newtonsoft.Json.Linq;
using WebReaper.Absctracts.Sinks;

namespace WebReaper.Sinks;

public class ConsoleSink : IScraperSink
{
    public Task Emit(JObject scrapedData) 
    {
        Console.WriteLine($"{scrapedData.ToString()}");
        return Task.CompletedTask;
    }
}