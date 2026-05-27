using WebReaper.Domain.PageActions;

namespace WebReaper.Core.Actions.Abstract;

/// <summary>
/// The resolution seam for <see cref="PageAction.SemanticAct"/> (ADR-0050):
/// given a natural-language <c>intent</c> string and the current
/// page's rendered HTML, return a concrete <see cref="PageAction"/> arm
/// (typically <see cref="PageAction.Click"/>,
/// <see cref="PageAction.WaitForSelector"/>, <see cref="PageAction.Wait"/>, or
/// <see cref="PageAction.EvaluateExpression"/>) — or <c>null</c> if the intent
/// cannot be resolved against the current page.
/// <para>
/// The default registration is the no-op <c>NullActionResolver</c> (returns
/// <c>null</c>); the LLM-backed implementation ships in the
/// <c>WebReaper.AI</c> satellite as <c>LlmActionResolver</c>. Resolutions are
/// cached per crawl by intent string in the Puppeteer transport — the
/// resolver is invoked once per intent, and the cached concrete arm is
/// dispatched on every subsequent page (the deterministic path is the hot
/// path, ADR-0046 / ADR-0047 pattern applied to actions).
/// </para>
/// <para>
/// The resolver must never return a <see cref="PageAction.SemanticAct"/> arm
/// (that would loop); the transport surfaces an unsupported return as a
/// <see cref="Concrete.SemanticActResolutionException"/>.
/// </para>
/// </summary>
public interface IActionResolver
{
    /// <summary>
    /// Resolve <paramref name="intent"/> to a concrete <see cref="PageAction"/>
    /// against <paramref name="pageHtml"/>, or <c>null</c> when the intent
    /// cannot be matched.
    /// </summary>
    /// <param name="intent">The natural-language intent string from
    /// <see cref="PageAction.SemanticAct.Intent"/>.</param>
    /// <param name="pageHtml">The rendered HTML of the current page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<PageAction?> ResolveAsync(
        string intent,
        string pageHtml,
        CancellationToken cancellationToken = default);
}
