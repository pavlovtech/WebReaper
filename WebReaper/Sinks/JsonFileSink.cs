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

        public Task EmitAsync(JObject scrapedData)
        {
            if(!IsInitialized)
            {
                Init();
            }

            entries.Add(scrapedData);

            return Task.CompletedTask;
        }

        public async Task HandleAsync()
        {
            await File.AppendAllTextAsync(filePath, "[");
            
            foreach(var entry in entries.GetConsumingEnumerable()) {
                await File.AppendAllTextAsync(filePath, $"{entry.ToString()},{Environment.NewLine}");
            }

            await File.AppendAllTextAsync(filePath, "]");
        }

        public void Init()
        {
            lock (_lock)
            {
                if(IsInitialized)
                {
                    return;
                }

                File.Delete(filePath);
                IsInitialized = true;
            
                _ = HandleAsync();
            }

            return;
        }
    }
}