using Newtonsoft.Json.Linq;

namespace WebReaper.Scraper.Absctract
{
    public interface IScraperSink
    {
         public Task Emit(JObject scrapedData);
    }
}