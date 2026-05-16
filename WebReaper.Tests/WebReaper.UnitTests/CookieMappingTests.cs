using System.Net;
using WebReaper.Extensions;

namespace WebReaper.UnitTests
{
    public class CookieMappingTests
    {
        [Fact]
        public void CarriesDomainPathSecureHttpOnly()
        {
            var container = new CookieContainer();
            container.Add(new Cookie("session", "abc", "/app", "example.com")
            {
                Secure = true,
                HttpOnly = true
            });

            var mapped = container.ToPuppeteerCookies("https://example.com/app");

            var c = Assert.Single(mapped);
            Assert.Equal("session", c.Name);
            Assert.Equal("abc", c.Value);
            Assert.Equal("example.com", c.Domain);
            Assert.Equal("/app", c.Path);
            Assert.True(c.Secure);
            Assert.True(c.HttpOnly);
            // Domain set => Url must not be set (Puppeteer rejects both).
            Assert.Null(c.Url);
        }

        [Fact]
        public void SessionCookieHasNoExpiry()
        {
            var container = new CookieContainer();
            container.Add(new Cookie("s", "1", "/", "example.com"));

            var mapped = container.ToPuppeteerCookies("https://example.com");

            Assert.Null(Assert.Single(mapped).Expires);
        }

        [Fact]
        public void PersistentCookieKeepsExpiryAsUnixSeconds()
        {
            var expiry = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var container = new CookieContainer();
            container.Add(new Cookie("s", "1", "/", "example.com") { Expires = expiry });

            var mapped = container.ToPuppeteerCookies("https://example.com");

            var expected = new DateTimeOffset(expiry).ToUnixTimeSeconds();
            Assert.Equal(expected, Assert.Single(mapped).Expires);
        }
    }
}
