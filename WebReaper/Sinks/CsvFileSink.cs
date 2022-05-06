using System.Collections.Concurrent;
using Newtonsoft.Json.Linq;
using WebReaper.Absctracts.Sinks;

namespace WebReaper.Sinks
{
    public class CsvFileSink : IScraperSink
    {
        private object _lock = new();

        private readonly string filePath;
        
        BlockingCollection<JObject> entries = new();

        private bool isInitialized = false;

        public CsvFileSink(string filePath)
        {
            this.filePath = filePath;
        }

        public async Task Emit(JObject scrapedData)
        {
            entries.Add(scrapedData);

            if(!isInitialized) {
                
                lock (_lock)
                {
                    isInitialized = true;

                    File.Delete(filePath);                    
                }

                var flattened = scrapedData
                        .Descendants()
                        .OfType<JValue>()
                        .Select(jv => jv.Path.Remove(0, jv.Path.LastIndexOf(".")+1));

                    var header = string.Join(",", flattened) + Environment.NewLine;

                await File.AppendAllTextAsync(filePath, header);

                _ = Handle();
            }
        }

        protected async Task Handle()
        {
            foreach(var entry in entries.GetConsumingEnumerable()) {

                var flattened = entry
                    .Descendants()
                    .OfType<JValue>()
                    .Select(p => $"\"{p.Value?.ToString()?.Replace("\"", "\"\"")}\"");

                var csvLine = string.Join(",", flattened);

                await File.AppendAllTextAsync(filePath, $"{csvLine}{Environment.NewLine}");
            }
        }
    }
}