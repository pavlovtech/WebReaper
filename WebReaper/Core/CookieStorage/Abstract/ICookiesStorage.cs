using System.Net;

namespace WebReaper.Core.CookieStorage.Abstract;

/// <summary>
/// The crawl's cookie jar, so an authenticated session survives across pages
/// (and, distributed, is shared across workers). The page-load transport
/// fetches the container and builds its HTTP handler around it, so stored
/// cookies are actually applied (the ADR-0004 build-after-fetch ordering). The
/// durable adapters extend the <c>CookieStore</c> payload shell (ADR-0003);
/// satellites bind here (<c>RedisCookieStorage</c>,
/// <c>MongoDbCookieStorage</c>). In-memory by default.
/// </summary>
public interface ICookiesStorage
{
    /// <summary>
    /// Store (replace) the session cookies — e.g. after a login / cookie-set
    /// step — so subsequent page loads carry them.
    /// </summary>
    Task AddAsync(CookieContainer cookieCollection);

    /// <summary>
    /// The current session cookies. The page-load transport calls this and
    /// constructs its handler around the returned container.
    /// </summary>
    Task<CookieContainer> GetAsync();
}
