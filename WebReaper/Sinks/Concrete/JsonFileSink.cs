using System.Collections.Concurrent;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WebReaper.Sinks.Abstract;

namespace WebReaper.Sinks.Concrete
{
    public class JsonLinesFileSink : IScraperSink
    {
        private object _lock = new();

        private readonly string filePath;

        BlockingCollection<JObject> entries = new();

        public bool IsInitialized { get; set; } = false;

        public JsonLinesFileSink(string filePath)
        {
            this.filePath = filePath;
            Init();
        }

        public Task EmitAsync(JObject scrapedData, CancellationToken cancellationToken = default)
        {
            if (!IsInitialized)
            {
                Init(cancellationToken);
            }

            entries.Add(scrapedData, cancellationToken);

            return Task.CompletedTask;
        }

        public async Task HandleAsync(CancellationToken cancellationToken = default)
        {
            foreach (var entry in entries.GetConsumingEnumerable(cancellationToken))
            {
                await File.AppendAllTextAsync(filePath, $"{entry.ToString(Formatting.None)}{Environment.NewLine}", cancellationToken);
            }
        }

        public void Init(CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                if (IsInitialized)
                {
                    return;
                }

                File.Delete(filePath);
            }

            _ = Task.Run(async() => await HandleAsync(cancellationToken));

            IsInitialized = true;

            return;
        }
    }
}