using Newtonsoft.Json.Linq;

namespace WebReaper.Sinks.Abstract
{
    public interface IScraperSink
    {
        public Task EmitAsync(JObject scrapedData);
    }
}