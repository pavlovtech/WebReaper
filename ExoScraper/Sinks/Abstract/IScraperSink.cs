using ExoScraper.Sinks.Models;
using Newtonsoft.Json.Linq;

namespace ExoScraper.Sinks.Abstract;

public interface IScraperSink
{
    public Task EmitAsync(ParsedData entity, CancellationToken cancellationToken = default);
}