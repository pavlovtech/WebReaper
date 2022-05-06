using System.Collections.Concurrent;
using Newtonsoft.Json.Linq;
using WebReaper.Absctracts.Sinks;

namespace WebReaper.Sinks
{
    public class JsonFileSink : IScraperSink
    {
        private object _lock = new();

        private readonly string filePath;
        
        BlockingCollection<JObject> entries = new();

        private bool isInitialized = false;

        public JsonFileSink(string filePath) => this.filePath = filePath;

        protected ConcurrentBag<string> AllData { get; set; } = new();

        public async Task Emit(JObject scrapedData)
        {
            entries.Add(scrapedData);

            if(!isInitialized)
            {
                lock (_lock)
                {
                    isInitialized = true;
                    File.Delete(filePath);
                
                    _ = Handle();
                }
            }
        }

        public async Task Handle()
        {
            foreach(var entry in entries.GetConsumingEnumerable())
            {
                AllData.Add(entry.ToString());

                var data = string.Join($",{Environment.NewLine}", AllData);

                await File.WriteAllTextAsync(filePath, $"[{Environment.NewLine}{data}{Environment.NewLine}]");
            }
        }
    }
}