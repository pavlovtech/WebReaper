using WebReaper.Sinks.Abstract;
using WebReaper.Sinks.Models;

namespace WebReaper.Sinks.Concrete;

/// <summary>
/// ADR-0038: wraps an <see cref="Action{ParsedData}"/> as an
/// <see cref="IScraperSink"/> — it backs <c>ScraperEngineBuilder.Subscribe</c>,
/// which is sugar for registering a delegate destination rather than a
/// separate notification seam. Tier-2 internal (ADR-0023): reached only
/// through the builder.
/// </summary>
internal sealed class DelegateSink : IScraperSink
{
    private readonly Action<ParsedData> _handler;

    public DelegateSink(Action<ParsedData> handler) => _handler = handler;

    public bool DataCleanupOnStart { get; set; }

    public Task EmitAsync(ParsedData entity, CancellationToken cancellationToken = default)
    {
        _handler(entity);
        return Task.CompletedTask;
    }
}
