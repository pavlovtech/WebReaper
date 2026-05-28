using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WebReaper.TestServer;

/// <summary>
/// An in-process Kestrel site serving deterministic fixtures for the
/// integration + performance suites. Boots on an ephemeral loopback port
/// (<c>http://127.0.0.1:0</c>), so many instances can run in parallel without
/// a port clash. Framework-agnostic (no xUnit dependency) so the perf console
/// app can host it too; xUnit suites wrap it in a thin
/// <c>IAsyncLifetime</c> fixture.
///
/// Every fixture is fixed-content so a scrape can assert *exact* field values
/// rather than "more than one record came back".
/// </summary>
public sealed class LocalTestSite : IAsyncDisposable
{
    private readonly WebApplication _app;

    /// <summary>Per-key hit counters for the <c>/fail</c> endpoint, so a test
    /// can assert how many times the retry policy actually re-requested.</summary>
    private readonly ConcurrentDictionary<string, int> _failHits = new();

    private LocalTestSite(WebApplication app) => _app = app;

    /// <summary>The resolved base URL, e.g. <c>http://127.0.0.1:53124</c>. Set
    /// once the OS assigns the ephemeral port (after <c>StartAsync</c>); the
    /// endpoint lambdas read it lazily per-request, so it is always populated
    /// by the time the first request arrives.</summary>
    public string BaseUrl { get; private set; } = "";

    /// <summary>Absolute URL for a site-relative path (leading slash optional).</summary>
    public string Url(string relative) => $"{BaseUrl}/{relative.TrimStart('/')}";

    /// <summary>How many times <c>/fail</c> was hit for <paramref name="key"/> —
    /// the observable retry count.</summary>
    public int FailHits(string key) => _failHits.TryGetValue(key, out var n) ? n : 0;

    public static async Task<LocalTestSite> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();

        var app = builder.Build();
        // Ephemeral loopback port (port 0 → OS-assigned) so parallel instances
        // never clash. Set on the app rather than the builder to avoid the
        // IWebHostBuilder.UseUrls extension surface.
        app.Urls.Clear();
        app.Urls.Add("http://127.0.0.1:0");

        var site = new LocalTestSite(app);
        site.MapEndpoints(app);

        await app.StartAsync();

        // Resolve the actual bound address (port 0 → OS-assigned).
        var address = app.Urls.FirstOrDefault()
            ?? throw new InvalidOperationException("Test site did not bind an address.");
        site.BaseUrl = address.TrimEnd('/');
        return site;
    }

    private void MapEndpoints(WebApplication app)
    {
        // ---- /static : one fixed product page (schema + markdown asserts) ----
        app.MapGet("/static", () => Html($@"
<!doctype html><html><head><title>Static Page</title></head>
<body>
  <h1 class=""title"">Widget Pro 3000</h1>
  <div class=""price"">$49.99</div>
  <div class=""description"">A deterministic test product.</div>
  <a class=""offsite"" href=""https://example.com/elsewhere"">offsite</a>
</body></html>"));

        // ---- /list?page=N : paginated index (Follow / Paginate / limits) ----
        // page 1 → items 1..3 + next; page 2 → items 4..6, no next.
        app.MapGet("/list", (int page = 1) =>
        {
            var firstId = (page - 1) * 3 + 1;
            var items = string.Concat(Enumerable.Range(firstId, 3).Select(id =>
                $@"<li><a class=""item"" href=""/item/{id}"">Item {id}</a></li>"));
            var next = page < 2
                ? $@"<a class=""next"" href=""/list?page={page + 1}"">Next</a>"
                : "";
            return Html($@"
<!doctype html><html><head><title>List page {page}</title></head>
<body><ul>{items}</ul>{next}</body></html>");
        });

        // ---- /item/{id} : leaf target page ----
        app.MapGet("/item/{id:int}", (int id) => Html($@"
<!doctype html><html><head><title>Item {id}</title></head>
<body>
  <h1 class=""title"">Item {id}</h1>
  <div class=""price"">${id}.00</div>
</body></html>"));

        // ---- /genlist?count=N : N item links (throughput / scale harness) ----
        app.MapGet("/genlist", (int count = 100) =>
        {
            var items = string.Concat(Enumerable.Range(1, count).Select(i =>
                $@"<li><a class=""gen"" href=""/gen/{i}"">Gen {i}</a></li>"));
            return Html($@"<!doctype html><html><body><ul>{items}</ul></body></html>");
        });

        // ---- /gen/{id} : tiny leaf page for the generated index ----
        app.MapGet("/gen/{id:int}", (int id) => Html(
            $@"<!doctype html><html><body><h1 class=""title"">Gen {id}</h1></body></html>"));

        // ---- /spa : content injected by JS (HTTP misses it; browser sees it) ----
        app.MapGet("/spa", () => Html(@"
<!doctype html><html><head><title>SPA</title></head>
<body>
  <div id=""root""></div>
  <script>
    document.addEventListener('DOMContentLoaded', function () {
      document.getElementById('root').innerHTML =
        '<h1 class=""title"">Hydrated Title</h1><div class=""price"">$7.77</div>';
    });
  </script>
</body></html>"));

        // ---- /slow?ms=N : delayed response (timeout / throughput) ----
        app.MapGet("/slow", async (int ms = 1000) =>
        {
            await Task.Delay(ms);
            return Results.Content(
                @"<!doctype html><html><body><h1 class=""title"">Slow</h1></body></html>",
                "text/html");
        });

        // ---- /fail?key=K&status=S&times=N : fail the first N hits, then 200 ----
        // Hit count is recorded per key so a test can read back the retry count.
        app.MapGet("/fail", (string key = "default", int status = 500, int times = 1) =>
        {
            var hit = _failHits.AddOrUpdate(key, 1, (_, n) => n + 1);
            if (hit <= times)
                return Results.StatusCode(status);
            return Results.Content(
                @"<!doctype html><html><body><h1 class=""title"">Recovered</h1></body></html>",
                "text/html");
        });

        // ---- robots.txt + sitemap.xml : map() discovery ----
        app.MapGet("/robots.txt", () => Results.Content(
            $"User-agent: *\nAllow: /\nSitemap: {BaseUrl}/sitemap.xml\n", "text/plain"));

        app.MapGet("/sitemap.xml", () => Results.Content($@"<?xml version=""1.0"" encoding=""UTF-8""?>
<urlset xmlns=""http://www.sitemaps.org/schemas/sitemap/0.9"">
  <url><loc>{BaseUrl}/item/1</loc></url>
  <url><loc>{BaseUrl}/item/2</loc></url>
  <url><loc>{BaseUrl}/item/3</loc></url>
  <url><loc>{BaseUrl}/static</loc></url>
</urlset>", "application/xml"));

        // ---- root : a few internal links + one offsite (map / crawl) ----
        app.MapGet("/", () => Html($@"
<!doctype html><html><head><title>Home</title></head>
<body>
  <a href=""/static"">static</a>
  <a href=""/list?page=1"">list</a>
  <a href=""/item/1"">item 1</a>
  <a href=""https://example.com/external"">external</a>
</body></html>"));

        // ---- /cookie : sets a cookie (cookie-storage adapters) ----
        app.MapGet("/cookie", (HttpContext ctx) =>
        {
            ctx.Response.Cookies.Append("wr_session", "abc123");
            return Results.Content(
                @"<!doctype html><html><body><h1 class=""title"">Cookie set</h1></body></html>",
                "text/html");
        });

        // ---- /challenge : emits a bot-check marker (BotCheckDetector) ----
        app.MapGet("/challenge", () => Html(@"
<!doctype html><html><head><title>Just a moment...</title></head>
<body>
  <h1>Checking your browser before accessing the site.</h1>
  <div id=""cf-challenge-running"">Cloudflare</div>
  <noscript>Please enable JavaScript and cookies to continue.</noscript>
</body></html>"));
    }

    private static IResult Html(string body) => Results.Content(body, "text/html");

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
