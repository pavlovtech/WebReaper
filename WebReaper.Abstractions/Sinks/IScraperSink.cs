using Newtonsoft.Json.Linq;

namespace WebReaper.Absctracts.Sinks
{
    public interface IScraperSink
    {
        public Task Emit(JObject scrapedData);
    }
}