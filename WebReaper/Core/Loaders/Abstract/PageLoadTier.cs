using WebReaper.Domain.Selectors;

namespace WebReaper.Core.Loaders.Abstract;

/// <summary>
/// One rung of the <c>EscalatingPageLoader</c>'s ladder (ADR-0083): a
/// <see cref="IPageLoadTransport"/> paired with the <see cref="PageType"/> it
/// serves. The ladder is ordered lowest to highest — HTTP (Static) then the
/// headless-browser transports (Dynamic) — and the loader climbs it on a block.
/// <para>
/// The <see cref="PageType"/> tag is how the loader picks a page's starting rung:
/// a Static page may start on any tier (an HTTP tier serves it, and a browser
/// tier renders it too), but a Dynamic page must skip the HTTP tier because an
/// HTTP fetch returns un-rendered HTML. It does not change what a transport does
/// — a transport is its own mechanism and ignores <c>PageRequest.PageType</c>.
/// </para>
/// </summary>
/// <param name="PageType">The load mode this rung serves: <see cref="PageType.Static"/>
/// for the HTTP transport, <see cref="PageType.Dynamic"/> for a headless-browser
/// transport.</param>
/// <param name="Transport">The mechanism for this rung.</param>
internal sealed record PageLoadTier(PageType PageType, IPageLoadTransport Transport);
