using System.Text.Json;
using System.Text.Json.Serialization;
using WebReaper.PlaygroundApi.Scraping;

var builder = WebApplication.CreateBuilder(args);

// CORS: in production the browser hits the same-origin Vercel edge proxy, not
// this backend directly, so cross-origin access is limited to the known site
// origins. Set PLAYGROUND_ALLOWED_ORIGINS (comma-separated) to lock it down;
// unset => any-origin, for local dev only. EventSource sends no credentials.
var allowedOrigins = (Environment.GetEnvironmentVariable("PLAYGROUND_ALLOWED_ORIGINS") ?? string.Empty)
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    policy.WithMethods("GET").AllowAnyHeader();
    if (allowedOrigins.Length > 0)
        policy.WithOrigins(allowedOrigins);
    else
        policy.SetIsOriginAllowed(_ => true); // dev fallback
}));

// Once the Vercel edge gate (Turnstile + rate limit) fronts this service, the
// backend must refuse direct public traffic, or the gate is trivially bypassed
// by hitting the Fly URL. When PLAYGROUND_BACKEND_SECRET is set, the scrape
// endpoint requires it (the edge injects X-Playground-Secret); unset => local
// dev, allowed. /health stays open for the Fly health check.
var backendSecret = Environment.GetEnvironmentVariable("PLAYGROUND_BACKEND_SECRET");

builder.Services.AddSingleton<TierAScraper>();

var app = builder.Build();
app.UseCors();

var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
};

app.MapGet("/health", () => Results.Text("ok"));

// Tier A live scrape: GET so the browser EventSource can consume it directly.
app.MapGet("/scrape/stream", async (HttpContext context, TierAScraper scraper, string? url, CancellationToken ct) =>
{
    // Reject direct public traffic when fronted by the edge gate (see above).
    var provided = context.Request.Headers["X-Playground-Secret"].ToString();
    if (!string.IsNullOrEmpty(backendSecret) && !string.Equals(provided, backendSecret, StringComparison.Ordinal))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }

    var response = context.Response;
    response.Headers.ContentType = "text/event-stream";
    response.Headers.CacheControl = "no-cache";
    response.Headers["X-Accel-Buffering"] = "no"; // disable proxy buffering so events stream

    await foreach (var climbEvent in scraper.StreamAsync(url ?? string.Empty, ct))
    {
        var data = JsonSerializer.Serialize(climbEvent, jsonOptions);
        await response.WriteAsync($"data: {data}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }
});

app.Run();
