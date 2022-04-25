using System.Collections.Concurrent;
using Newtonsoft.Json.Linq;
using WebReaper.Sinks.Absctract;

namespace WebReaper.Sinks.Concrete
{
    public class CsvFileSink : IScraperSink
    {
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
                isInitialized = true;
                File.Delete(filePath);

                var flattened = scrapedData
                    .Descendants()
                    .OfType<JValue>()
                    .Select(jv => jv.Path.Remove(0, jv.Path.LastIndexOf(".")+1));

                var header = string.Join(",", flattened) + Environment.NewLine;
                
                await File.AppendAllTextAsync(filePath, header);
                _ = Handle();
            }
        }

        public async ValueTask Handle()
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