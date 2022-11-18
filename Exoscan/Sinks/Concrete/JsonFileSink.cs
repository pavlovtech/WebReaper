using System.Collections.Concurrent;
using Exoscan.Sinks.Abstract;
using Exoscan.Sinks.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Exoscan.Sinks.Concrete;

public class JsonLinesFileSink : IScraperSink
{
    private readonly object _lock = new();

    private readonly string filePath;

    private readonly BlockingCollection<JObject> entries = new();

    private bool IsInitialized { get; set; }

    public JsonLinesFileSink(string filePath)
    {
        this.filePath = filePath;
        Init();
    }

    public Task EmitAsync(ParsedData entity, CancellationToken cancellationToken = default)
    {
        if (!IsInitialized)
        {
            Init(cancellationToken);
        }
        
        entity.Data["url"] = entity.Url;

        entries.Add(entity.Data, cancellationToken);

        return Task.CompletedTask;
    }

    private async Task HandleAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in entries.GetConsumingEnumerable(cancellationToken))
        {
            await File.AppendAllTextAsync(filePath, $"{entry.ToString(Formatting.None)}{Environment.NewLine}", cancellationToken);
        }
    }

    private void Init(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (IsInitialized)
            {
                return;
            }

            //File.Delete(filePath);
        }

        _ = Task.Run(async() => await HandleAsync(cancellationToken), cancellationToken);

        IsInitialized = true;
    }
}