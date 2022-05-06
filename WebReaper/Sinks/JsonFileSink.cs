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

        public Task EmitAsync(JObject scrapedData)
        {
            entries.Add(scrapedData);

            if(!isInitialized)
            {
                lock (_lock)
                {
                    isInitialized = true;
                    File.Delete(filePath);
                
                    _ = HandleAsync();
                }
            }

            return Task.CompletedTask;
        }

        public async Task HandleAsync()
        {
            foreach(var entry in entries.GetConsumingEnumerable()) {
                await File.AppendAllTextAsync(filePath, $"{entry.ToString()},{Environment.NewLine}");
            }

            await File.AppendAllTextAsync(filePath, "]");
        }
    }
}