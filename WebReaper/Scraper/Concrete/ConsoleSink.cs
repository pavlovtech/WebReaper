using Newtonsoft.Json.Linq;
using WebReaper.Scraper.Absctract;

public class ConsoleSink : IScraperSink
{
    public Task Emit(JObject scrapedData) 
    {
        Console.WriteLine($"{scrapedData.ToString()}");
        return Task.CompletedTask;
    }
}