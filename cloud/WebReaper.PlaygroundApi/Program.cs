using System.Text.Json;
using System.Text.Json.Serialization;
using WebReaper.PlaygroundApi.Scraping;

var builder = WebApplication.CreateBuilder(args);

// Dev CORS: the climb component (localhost:3000 / webreaper.ai) reads this SSE
// cross-origin. EventSource sends no credentials, so any-origin is safe here;
// tighten to the known site origins before deploy (Phase 1 step 4).
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .SetIsOriginAllowed(_ => true)
    .WithMethods("GET")
    .AllowAnyHeader()));

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
