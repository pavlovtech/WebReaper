using Newtonsoft.Json.Linq;

namespace WebReaper.Absctracts.Sinks
{
    public interface IScraperSink
    {
        public Task EmitAsync(JObject scrapedData);
        public Task InitAsync();
        public bool IsInitialized { get; }
    }
}