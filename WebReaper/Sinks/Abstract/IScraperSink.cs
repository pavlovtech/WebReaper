using Newtonsoft.Json.Linq;

namespace WebReaper.Core.Sinks.Abstract
{
    public interface IScraperSink
    {
        public Task EmitAsync(JObject scrapedData);
    }
}