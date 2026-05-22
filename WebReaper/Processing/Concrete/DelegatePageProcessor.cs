using WebReaper.Processing.Abstract;

namespace WebReaper.Processing.Concrete;

/// <summary>
/// ADR-0038: wraps a delegate as an <see cref="IPageProcessor"/> so a stateless
/// processor needs no class — it backs the <c>ScraperEngineBuilder.Process</c>
/// delegate overloads. Tier-2 internal (ADR-0023): reached only through the
/// builder, never named by a consumer.
/// </summary>
internal sealed class DelegatePageProcessor : IPageProcessor
{
    private readonly Func<PageContext, CancellationToken, ValueTask<PageVerdict>> _process;

    public DelegatePageProcessor(Func<PageContext, CancellationToken, ValueTask<PageVerdict>> process)
        => _process = process;

    public ValueTask<PageVerdict> ProcessAsync(PageContext context, CancellationToken cancellationToken)
        => _process(context, cancellationToken);
}
