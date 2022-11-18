using Exoscan.Sinks.Models;
using Newtonsoft.Json.Linq;

namespace Exoscan.Sinks.Abstract;

public interface IScraperSink
{
    public Task EmitAsync(ParsedData entity, CancellationToken cancellationToken = default);
}