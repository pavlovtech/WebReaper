using Newtonsoft.Json.Linq;

namespace WebReaper.Abstractions.Sinks
{
    public interface IScraperSink
    {
        public Task EmitAsync(JObject scrapedData);
    }
}