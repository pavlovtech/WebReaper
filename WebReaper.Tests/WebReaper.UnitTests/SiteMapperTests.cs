using System.Net;
using System.Text;
using WebReaper.Core.Mapping;

namespace WebReaper.UnitTests;

// ADR-0042: the URL-discovery seam and its default adapter. Tests run
// against a stub HttpMessageHandler — no real network — pinning sitemap
// parsing, sitemap-index one-level recursion, robots.txt Sitemap:
// extraction, root-page link extraction, host filter, Search filter,
// MaxUrls cap, ordering, and best-effort failure handling.
public class SiteMapperTests
{
    private static SiteMapper Make(StubHttpHandler handler) =>
        new(() => handler);

    [Fact]
    public async Task Reads_sitemap_xml_at_the_default_location()
    {
        var handler = new StubHttpHandler
        {
            // No robots.txt → fall back to /sitemap.xml.
            ["https://x.test/robots.txt"] = (HttpStatusCode.NotFound, ""),
            ["https://x.test/sitemap.xml"] = (HttpStatusCode.OK, @"<?xml version='1.0' encoding='UTF-8'?>
<urlset xmlns='http://www.sitemaps.org/schemas/sitemap/0.9'>
  <url><loc>https://x.test/a</loc></url>
  <url><loc>https://x.test/b</loc></url>
</urlset>"),
            ["https://x.test/"] = (HttpStatusCode.OK,
                "<html><body><a href='/c'>c</a></body></html>")
        };

        var urls = await Make(handler).MapAsync("https://x.test/");

        Assert.Contains("https://x.test/a", urls);
        Assert.Contains("https://x.test/b", urls);
        Assert.Contains("https://x.test/c", urls);
    }

    [Fact]
    public async Task Honours_robots_txt_sitemap_directive()
    {
        var handler = new StubHttpHandler
        {
            ["https://x.test/robots.txt"] = (HttpStatusCode.OK,
                "User-agent: *\nDisallow:\nSitemap: https://x.test/custom-sitemap.xml\n"),
            ["https://x.test/custom-sitemap.xml"] = (HttpStatusCode.OK, @"
<urlset xmlns='http://www.sitemaps.org/schemas/sitemap/0.9'>
  <url><loc>https://x.test/from-custom</loc></url>
</urlset>"),
            ["https://x.test/"] = (HttpStatusCode.OK, "<html><body></body></html>")
        };

        var urls = await Make(handler).MapAsync("https://x.test/");

        Assert.Contains("https://x.test/from-custom", urls);
        // The custom one was used; the default /sitemap.xml was not.
        Assert.DoesNotContain("https://x.test/sitemap.xml", handler.Requests);
    }

    [Fact]
    public async Task Recurses_one_level_into_a_sitemap_index()
    {
        var handler = new StubHttpHandler
        {
            ["https://x.test/robots.txt"] = (HttpStatusCode.NotFound, ""),
            ["https://x.test/sitemap.xml"] = (HttpStatusCode.OK, @"
<sitemapindex xmlns='http://www.sitemaps.org/schemas/sitemap/0.9'>
  <sitemap><loc>https://x.test/sitemap-1.xml</loc></sitemap>
  <sitemap><loc>https://x.test/sitemap-2.xml</loc></sitemap>
</sitemapindex>"),
            ["https://x.test/sitemap-1.xml"] = (HttpStatusCode.OK, @"
<urlset xmlns='http://www.sitemaps.org/schemas/sitemap/0.9'>
  <url><loc>https://x.test/from-1</loc></url>
</urlset>"),
            ["https://x.test/sitemap-2.xml"] = (HttpStatusCode.OK, @"
<urlset xmlns='http://www.sitemaps.org/schemas/sitemap/0.9'>
  <url><loc>https://x.test/from-2</loc></url>
</urlset>"),
            ["https://x.test/"] = (HttpStatusCode.OK, "<html><body></body></html>")
        };

        var urls = await Make(handler).MapAsync("https://x.test/");

        Assert.Contains("https://x.test/from-1", urls);
        Assert.Contains("https://x.test/from-2", urls);
    }

    [Fact]
    public async Task Extracts_root_page_anchors_and_resolves_relative_hrefs()
    {
        var handler = new StubHttpHandler
        {
            ["https://x.test/robots.txt"] = (HttpStatusCode.NotFound, ""),
            ["https://x.test/sitemap.xml"] = (HttpStatusCode.NotFound, ""),
            ["https://x.test/"] = (HttpStatusCode.OK,
                "<html><body>" +
                "<a href='/a'>a</a>" +
                "<a href='b'>b</a>" +
                "<a href='https://x.test/c?q=1'>c</a>" +
                "</body></html>")
        };

        var urls = await Make(handler).MapAsync("https://x.test/");

        Assert.Contains("https://x.test/a", urls);
        Assert.Contains("https://x.test/b", urls);
        Assert.Contains("https://x.test/c?q=1", urls);
    }

    [Fact]
    public async Task Filters_offsite_links_by_default()
    {
        var handler = new StubHttpHandler
        {
            ["https://x.test/robots.txt"] = (HttpStatusCode.NotFound, ""),
            ["https://x.test/sitemap.xml"] = (HttpStatusCode.NotFound, ""),
            ["https://x.test/"] = (HttpStatusCode.OK,
                "<html><body>" +
                "<a href='/inside'>inside</a>" +
                "<a href='https://elsewhere.test/outside'>outside</a>" +
                "</body></html>")
        };

        var urls = await Make(handler).MapAsync("https://x.test/");

        Assert.Contains("https://x.test/inside", urls);
        Assert.DoesNotContain("https://elsewhere.test/outside", urls);
    }

    [Fact]
    public async Task Allows_offsite_links_when_opted_in()
    {
        var handler = new StubHttpHandler
        {
            ["https://x.test/robots.txt"] = (HttpStatusCode.NotFound, ""),
            ["https://x.test/sitemap.xml"] = (HttpStatusCode.NotFound, ""),
            ["https://x.test/"] = (HttpStatusCode.OK,
                "<html><body>" +
                "<a href='https://elsewhere.test/outside'>outside</a>" +
                "</body></html>")
        };

        var urls = await Make(handler).MapAsync("https://x.test/",
            new MapOptions(AllowOffsite: true));

        Assert.Contains("https://elsewhere.test/outside", urls);
    }

    [Fact]
    public async Task Search_substring_filter_is_case_insensitive()
    {
        var handler = new StubHttpHandler
        {
            ["https://x.test/robots.txt"] = (HttpStatusCode.NotFound, ""),
            ["https://x.test/sitemap.xml"] = (HttpStatusCode.OK, @"
<urlset xmlns='http://www.sitemaps.org/schemas/sitemap/0.9'>
  <url><loc>https://x.test/Blog/post-1</loc></url>
  <url><loc>https://x.test/about</loc></url>
  <url><loc>https://x.test/blog/post-2</loc></url>
</urlset>"),
            ["https://x.test/"] = (HttpStatusCode.OK, "<html><body></body></html>")
        };

        var urls = await Make(handler).MapAsync("https://x.test/",
            new MapOptions(Search: "blog"));

        Assert.Contains("https://x.test/Blog/post-1", urls);
        Assert.Contains("https://x.test/blog/post-2", urls);
        Assert.DoesNotContain("https://x.test/about", urls);
    }

    [Fact]
    public async Task Max_urls_caps_the_result()
    {
        var handler = new StubHttpHandler
        {
            ["https://x.test/robots.txt"] = (HttpStatusCode.NotFound, ""),
            ["https://x.test/sitemap.xml"] = (HttpStatusCode.OK, @"
<urlset xmlns='http://www.sitemaps.org/schemas/sitemap/0.9'>
  <url><loc>https://x.test/1</loc></url>
  <url><loc>https://x.test/2</loc></url>
  <url><loc>https://x.test/3</loc></url>
  <url><loc>https://x.test/4</loc></url>
</urlset>"),
            ["https://x.test/"] = (HttpStatusCode.OK, "<html><body></body></html>")
        };

        var urls = await Make(handler).MapAsync("https://x.test/",
            new MapOptions(MaxUrls: 2));

        Assert.Equal(2, urls.Count);
    }

    [Fact]
    public async Task Sitemap_urls_come_before_root_page_links_in_the_order()
    {
        var handler = new StubHttpHandler
        {
            ["https://x.test/robots.txt"] = (HttpStatusCode.NotFound, ""),
            ["https://x.test/sitemap.xml"] = (HttpStatusCode.OK, @"
<urlset xmlns='http://www.sitemaps.org/schemas/sitemap/0.9'>
  <url><loc>https://x.test/from-sitemap</loc></url>
</urlset>"),
            ["https://x.test/"] = (HttpStatusCode.OK,
                "<a href='/from-anchor'>x</a>")
        };

        var urls = await Make(handler).MapAsync("https://x.test/");

        Assert.Equal("https://x.test/from-sitemap", urls[0]);
        Assert.Equal("https://x.test/from-anchor", urls[1]);
    }

    [Fact]
    public async Task Duplicates_collapse_in_favour_of_first_seen()
    {
        var handler = new StubHttpHandler
        {
            ["https://x.test/robots.txt"] = (HttpStatusCode.NotFound, ""),
            ["https://x.test/sitemap.xml"] = (HttpStatusCode.OK, @"
<urlset xmlns='http://www.sitemaps.org/schemas/sitemap/0.9'>
  <url><loc>https://x.test/shared</loc></url>
</urlset>"),
            ["https://x.test/"] = (HttpStatusCode.OK,
                "<a href='/shared'>shared</a>")
        };

        var urls = await Make(handler).MapAsync("https://x.test/");

        Assert.Single(urls, "https://x.test/shared");
    }

    [Fact]
    public async Task Malformed_sitemap_xml_is_skipped_not_thrown()
    {
        var handler = new StubHttpHandler
        {
            ["https://x.test/robots.txt"] = (HttpStatusCode.NotFound, ""),
            ["https://x.test/sitemap.xml"] = (HttpStatusCode.OK, "<not valid xml"),
            ["https://x.test/"] = (HttpStatusCode.OK,
                "<a href='/fallback'>f</a>")
        };

        var urls = await Make(handler).MapAsync("https://x.test/");

        Assert.Contains("https://x.test/fallback", urls);
    }

    [Fact]
    public async Task Include_sitemap_false_skips_sitemap_discovery()
    {
        var handler = new StubHttpHandler
        {
            ["https://x.test/sitemap.xml"] = (HttpStatusCode.OK, @"
<urlset xmlns='http://www.sitemaps.org/schemas/sitemap/0.9'>
  <url><loc>https://x.test/from-sitemap</loc></url>
</urlset>"),
            ["https://x.test/"] = (HttpStatusCode.OK,
                "<a href='/from-anchor'>x</a>")
        };

        var urls = await Make(handler).MapAsync("https://x.test/",
            new MapOptions(IncludeSitemap: false));

        Assert.DoesNotContain("https://x.test/from-sitemap", urls);
        Assert.Contains("https://x.test/from-anchor", urls);
    }

    [Fact]
    public async Task Include_root_page_links_false_skips_anchor_extraction()
    {
        var handler = new StubHttpHandler
        {
            ["https://x.test/robots.txt"] = (HttpStatusCode.NotFound, ""),
            ["https://x.test/sitemap.xml"] = (HttpStatusCode.OK, @"
<urlset xmlns='http://www.sitemaps.org/schemas/sitemap/0.9'>
  <url><loc>https://x.test/from-sitemap</loc></url>
</urlset>"),
            ["https://x.test/"] = (HttpStatusCode.OK,
                "<a href='/from-anchor'>x</a>")
        };

        var urls = await Make(handler).MapAsync("https://x.test/",
            new MapOptions(IncludeRootPageLinks: false));

        Assert.Contains("https://x.test/from-sitemap", urls);
        Assert.DoesNotContain("https://x.test/from-anchor", urls);
    }

    // A deterministic, network-free HttpMessageHandler. Maps absolute
    // URLs (string) to a (status, body) tuple; throws if asked for a URL
    // that wasn't pre-registered (catches the "I expected to hit that"
    // class of bug at the test boundary).
    private sealed class StubHttpHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, (HttpStatusCode Status, string Body)> _responses = new();
        public List<string> Requests { get; } = new();

        public (HttpStatusCode Status, string Body) this[string url]
        {
            set => _responses[url] = value;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            Requests.Add(url);

            if (!_responses.TryGetValue(url, out var response))
            {
                throw new InvalidOperationException(
                    $"Unexpected request to {url} — pre-register it in the stub.");
            }

            return Task.FromResult(new HttpResponseMessage(response.Status)
            {
                Content = new StringContent(response.Body, Encoding.UTF8, "text/html")
            });
        }
    }
}
