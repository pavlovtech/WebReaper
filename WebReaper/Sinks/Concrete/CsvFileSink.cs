using System.Collections.Concurrent;
using Newtonsoft.Json.Linq;
using WebReaper.Sinks.Abstract;

namespace WebReaper.Sinks.Concrete
{
    public class CsvFileSink : IScraperSink
    {
        private object _lock = new();

        private readonly string filePath;

        BlockingCollection<JObject> entries = new();

        protected bool IsInitialized { get; set; } = false;

        public CsvFileSink(string filePath)
        {
            this.filePath = filePath;
        }

        public async Task EmitAsync(JObject scrapedData)
        {
            entries.Add(scrapedData);

            if (!IsInitialized)
            {

                lock (_lock)
                {
                    File.Delete(filePath);
                }

                var flattened = scrapedData
                        .Descendants()
                        .OfType<JValue>()
                        .Select(jv => jv.Path.Remove(0, jv.Path.LastIndexOf(".") + 1));

                var header = string.Join(",", flattened) + Environment.NewLine;

                await File.AppendAllTextAsync(filePath, header);

                IsInitialized = true;

                _ = Handle();
            }
        }

        protected async Task Handle()
        {
            foreach (var entry in entries.GetConsumingEnumerable())
            {

                var flattened = entry
                    .Descendants()
                    .OfType<JValue>()
                    .Select(p => $"\"{p.Value?.ToString()?.Replace("\"", "\"\"")}\"");

                var csvLine = string.Join(",", flattened);

                await File.AppendAllTextAsync(filePath, $"{csvLine}{Environment.NewLine}");
            }
        }

        public Task InitAsync() => Task.CompletedTask;
    }
}