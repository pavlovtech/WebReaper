using System.Net;
using PuppeteerSharp;

namespace WebReaper.Puppeteer;

public static class CookieExtensions
{
    /// <summary>
    /// Maps a <see cref="CookieContainer"/> to Puppeteer
    /// <see cref="CookieParam"/>s for the given target URL.
    ///
    /// Fixes issue #26: previously only Name/Value were copied and
    /// cookies were set on a blank page before navigation, so Puppeteer
    /// had no domain to attach them to and the login was lost. We now
    /// carry Domain/Path/Secure/HttpOnly/Expires, and fall back to the
    /// target <paramref name="url"/> when a cookie has no domain so it
    /// can be set before the first navigation.
    /// </summary>
    public static CookieParam[] ToPuppeteerCookies(this CookieContainer container, string url)
    {
        ArgumentNullException.ThrowIfNull(container);

        return container.GetAllCookies().Select(c =>
        {
            var hasDomain = !string.IsNullOrWhiteSpace(c.Domain);

            var param = new CookieParam
            {
                Name = c.Name,
                Value = c.Value,
                Path = string.IsNullOrEmpty(c.Path) ? "/" : c.Path,
                Secure = c.Secure,
                HttpOnly = c.HttpOnly
            };

            // Puppeteer rejects a cookie with both Url and Domain; pick one.
            if (hasDomain)
            {
                param.Domain = c.Domain;
            }
            else
            {
                param.Url = url;
            }

            // Persistent cookies only; session cookies stay null (Expires
            // is DateTime.MinValue for them).
            if (c.Expires != DateTime.MinValue)
            {
                param.Expires = new DateTimeOffset(c.Expires.ToUniversalTime()).ToUnixTimeSeconds();
            }

            return param;
        }).ToArray();
    }
}
