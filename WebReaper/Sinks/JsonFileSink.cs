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

        public bool IsInitialized { get; set; } = false;

        public JsonFileSink(string filePath) => this.filePath = filePath;

        public async Task EmitAsync(JObject scrapedData)
        {
            if(!IsInitialized)
            {
                await InitAsync();
            }

            entries.Add(scrapedData);
        }

        public async Task HandleAsync()
        {
            foreach(var entry in entries.GetConsumingEnumerable()) {
                await File.AppendAllTextAsync(filePath, $"{entry.ToString()},{Environment.NewLine}");
            }

            await File.AppendAllTextAsync(filePath, "]");
        }

        public Task InitAsync()
        {
            lock (_lock)
            {
                File.Delete(filePath);
                IsInitialized = true;
            
                _ = HandleAsync();
            }

            return Task.CompletedTask;
        }
    }
}