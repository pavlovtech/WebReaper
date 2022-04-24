using Newtonsoft.Json.Linq;
using WebReaper.Sinks.Absctract;

namespace WebReaper.Sinks.Concrete;
public class ConsoleSink : IScraperSink
{
    public Task Emit(JObject scrapedData) 
    {
        Console.WriteLine($"{scrapedData.ToString()}");
        return Task.CompletedTask;
    }
}