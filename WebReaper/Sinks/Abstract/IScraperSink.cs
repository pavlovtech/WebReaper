using WebReaper.Sinks.Models;

namespace WebReaper.Sinks.Abstract;

public interface IScraperSink
{
    public bool DataCleanupOnStart { get; set; }

    public Task EmitAsync(ParsedData entity, CancellationToken cancellationToken = default);
}