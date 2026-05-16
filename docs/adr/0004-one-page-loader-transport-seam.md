# One page-loader seam with internal transports; not a Static/Dynamic loader pair

Page loading was two seams plus two copy-pasted families. `IStaticPageLoader`
had **exactly one** implementation (`HttpStaticPageLoader`, itself a thin shell
over an `IPageRequester` triad); `IBrowserPageLoader` had two implementations
that were an ~80% copy. The Spider held both and dispatched on `PageType`
itself. The proxy/no-proxy choice had no home — it was re-decided in the
`SpiderBuilder.Build()` branch, across the requester triad, and across the
Puppeteer pair — and bugs had drifted into the copies.

Replaced by one seam: `IPageLoader.LoadAsync(PageRequest) → html`. Behind it,
a single `PageLoader` dispatches on `PageRequest.PageType` to one of two real
`IPageLoadTransport` adapters — `HttpPageLoadTransport` and
`BrowserPageLoadTransport`. Each transport is the one home for its mechanism's
client/launch quirks and takes an optional `IProxyProvider` (null = direct),
applied the transport's own way. The Spider holds one `IPageLoader` and is
loader-blind — it is now purely the crawl-step I/O shell ADR 0001 describes.
`SpiderBuilder` constructs the two transports (passing the possibly-null
provider) and the dispatcher; the proxy branch disappears.

This is the same shape as ADR 0002: the module the caller sees (`IPageLoader`)
is deep and single; the *real* variation lives at an internal seam
(`IPageLoadTransport`, two genuine adapters) plus the already-abstracted
`IProxyProvider`. `IStaticPageLoader` was a single-adapter seam — indirection
without variation, which ADR 0001/0002/0003 reject; keeping it (and deepening
only the leaves) would have left that shallow trunk standing.

**Breaking — major SemVer.** Removed public surface: `IStaticPageLoader`,
`IBrowserPageLoader`, `BrowserPageLoader`, `HttpStaticPageLoader`,
`PuppeteerPageLoader`, `PuppeteerPageLoaderWithProxies`, `IPageRequester`,
`PageRequester`, `ProxyPageRequester`, `RotatingProxyPageRequester`; the
`Spider(IStaticPageLoader, IBrowserPageLoader, …)` constructor; and the
`WithStaticPageLoader` / `WithBrowserPageLoader` builder methods (replaced by
`WithPageLoader(IPageLoader)`). The whole `WebReaper.HttpRequests` namespace is
deleted. Blast radius is internal: the only consumers were `Spider` and
`SpiderBuilder` (Examples, Misc, and tests use the fluent builder and are
source-compatible). Supersedes only ADR 0001's *incidental* two-loader Spider
construction — the `PageType` concept (Static vs Dynamic load mode) is
unchanged; it is now a `PageRequest` field instead of a choice between two
seams.

## Deliberate consequences (bugs fixed by construction — see CONTEXT.md "Flagged ambiguities")

- **The non-proxy static path now applies stored cookies.** `PageRequester`
  built its `SocketsHttpHandler`/`HttpClient` in its constructor, but
  `HttpStaticPageLoader.Load` set `CookieContainer` *after* construction (its
  own `// TODO move to init factory func`) — the handler never saw the cookies.
  The transport now fetches cookies, then builds the handler.
- **One canonical User-Agent.** The triad had two by copy-drift (Chrome 106 vs
  Comodo Dragon 16).
- **One canonical browser navigation wait: `Networkidle2`.** The Puppeteer pair
  used `DOMContentLoaded` vs `Networkidle2` with no design reason — accidental
  drift; `Networkidle2` is correct for the JS-rendered pages a browser
  transport exists to handle.
- **The dead `ProxyPageRequester` is gone.** It was never constructed by the
  builder and carried a `static client ??=` first-wins bug (the old-`RedisBase`
  family). Only two proxy modes were ever live: none, and provider-per-request.

## Out of scope (preserved as-is)

`BrowserPageLoader`'s page-action table handled only four of six
`PageActionType`s (`WaitForSelector`/`EvaluateExpression` throw at runtime).
That is a missing feature, not this duplication deepening; the
`BrowserPageLoadTransport` preserves the exact four-entry behaviour. Not fixed
here to keep scope tight (the ADR 0001/0002/0003 discipline).

## Considered options

- **One `IPageLoader` + internal `IPageLoadTransport` seam (chosen).** Smallest
  caller-facing interface; real variation (two transports, optional proxy)
  internal; dispatch has one home out of the Spider; every drifted bug becomes
  unrepresentable.
- **Two independent deepenings, keep both seams (rejected).** Deepen the
  requester triad into one `IPageRequester` and collapse the Puppeteer pair,
  but keep `IStaticPageLoader`/`IBrowserPageLoader`. Deepens the leaves while
  leaving the single-adapter `IStaticPageLoader` trunk and the Spider's
  dispatch in place — half the win, and it preserves a seam the project's own
  taste says shouldn't exist. (This was the pre-approval recommendation, made
  under an unstated source-compat constraint that was then lifted.)
- **One unified loader that also unifies proxy *application* (rejected).** A
  shared "apply proxy" step across HTTP and browser. The two apply proxy
  fundamentally differently (`SocketsHttpHandler.Proxy` vs a Chromium
  `--proxy-server=` arg + `page.AuthenticateAsync`); a shared step would be
  indirection over things that legitimately differ — ADR 0002's rejected
  `ILinkParser`-into-`ISchemaBackend` mistake.

## Accepted cost

`PageRequest` carries `PageActions`/`Headless` that the HTTP transport ignores
— a mildly fat request type. Judged strictly better than preserving a
single-adapter seam to avoid it; the unused fields are contained in one small
record.
