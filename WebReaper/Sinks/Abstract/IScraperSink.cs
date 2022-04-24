using Newtonsoft.Json.Linq;

namespace WebReaper.Sinks.Absctract
{
    public interface IScraperSink
    {
         public Task Emit(JObject scrapedData);
    }
}