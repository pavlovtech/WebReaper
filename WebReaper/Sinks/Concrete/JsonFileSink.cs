using System.Collections.Concurrent;
using Newtonsoft.Json.Linq;
using WebReaper.Sinks.Absctract;

namespace WebReaper.Sinks.Concrete
{
    public class JsonFileSink : IScraperSink
    {
        private readonly string filePath;
        
        BlockingCollection<JObject> entries = new();

        private bool isInitialized = false;

        public JsonFileSink(string filePath)
        {
            this.filePath = filePath;
        }

        public async Task Emit(JObject scrapedData)
        {
            entries.Add(scrapedData);

            if(!isInitialized) {
                isInitialized = true;
                File.Delete(filePath);
                
                await File.AppendAllTextAsync(filePath, "[");
                _ = Handle();
            }
        }

        public async ValueTask Handle()
        {
            foreach(var entry in entries.GetConsumingEnumerable()) {
                await File.AppendAllTextAsync(filePath, $"{entry.ToString()},{Environment.NewLine}");
            }

            await File.AppendAllTextAsync(filePath, "]");
        }
    }
}