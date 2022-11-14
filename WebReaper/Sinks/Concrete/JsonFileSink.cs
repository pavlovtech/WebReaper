using System.Collections.Concurrent;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WebReaper.Sinks.Abstract;
using WebReaper.Sinks.Models;

namespace WebReaper.Sinks.Concrete;

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

    public Task EmitAsync(ParsedData parsedData, CancellationToken cancellationToken = default)
    {
        if (!IsInitialized)
        {
            Init(cancellationToken);
        }
        
        parsedData.Data["url"] = parsedData.Url;
        parsedData.Data["siteId"] = parsedData.SiteId;
        
        entries.Add(parsedData.Data, cancellationToken);

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