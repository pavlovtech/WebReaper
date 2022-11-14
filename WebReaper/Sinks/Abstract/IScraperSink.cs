using Newtonsoft.Json.Linq;
using WebReaper.Sinks.Models;

namespace WebReaper.Sinks.Abstract;

public interface IScraperSink
{
    public Task EmitAsync(ParsedData data, CancellationToken cancellationToken = default);
}