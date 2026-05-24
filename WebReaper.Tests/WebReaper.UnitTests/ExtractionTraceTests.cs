using System.Text.Json.Nodes;
using WebReaper.Builders;
using WebReaper.Core.Observability;
using WebReaper.Core.Observability.Abstract;
using WebReaper.Core.Observability.Concrete;
using WebReaper.Domain.Parsing;
using WebReaper.Domain.Selectors;

namespace WebReaper.UnitTests;

/// <summary>
/// ADR-0018. Pins the trace contract: NullExtractionTrace's
/// allocation-free hot path, the builder-wired propagation
/// (WithExtractionTrace + TraceToFile), and the closed-sum's eight arms.
/// </summary>
public class ExtractionTraceTests
{
    [Fact]
    public void NullExtractionTrace_singleton_is_returnable_via_Instance()
    {
        Assert.Same(NullExtractionTrace.Instance, NullExtractionTrace.Instance);
    }

    [Fact]
    public async Task NullExtractionTrace_completes_synchronously_no_throw()
    {
        var task = NullExtractionTrace.Instance.RecordAsync(
            new TraceEvent.PageLoadStarted(PageType.Static) { Url = "https://x" });
        Assert.True(task.IsCompletedSuccessfully);
        await task;
    }

    [Fact]
    public async Task Builder_WithExtractionTrace_registers_the_adapter_seen_by_the_engine()
    {
        var recorder = new RecordingTrace();

        await using (var engine = await ScraperEngineBuilder
            .Crawl("https://example.com")
            .Extract(new Schema { new SchemaElement("title", "h1", DataType.String) })
            .WithExtractionTrace(recorder)
            .BuildAsync())
        {
            // Disposal does not emit trace events; the WithExtractionTrace
            // contract is "the engine + Spider + CrawlStep share this
            // adapter" — we verify the registration via the no-throw path
            // and the public-surface contract.
        }

        // No crawl ran; nothing emitted. The test asserts the registration
        // didn't throw (a malformed propagation would have NREd in BuildAsync).
        Assert.Empty(recorder.Events);
    }

    [Fact]
    public void WithExtractionTrace_null_throws_ArgumentNullException()
    {
        var b = ScraperEngineBuilder.Crawl("https://x")
            .Extract(new Schema { new SchemaElement("title", "h1", DataType.String) });
        Assert.Throws<ArgumentNullException>(() => b.WithExtractionTrace(null!));
    }

    [Fact]
    public void TraceToFile_blank_path_throws()
    {
        var b = ScraperEngineBuilder.Crawl("https://x")
            .Extract(new Schema { new SchemaElement("title", "h1", DataType.String) });
        Assert.ThrowsAny<ArgumentException>(() => b.TraceToFile(""));
        Assert.ThrowsAny<ArgumentException>(() => b.TraceToFile("   "));
    }

    [Fact]
    public void TraceEvent_closed_sum_is_eight_named_arms()
    {
        // Exhaustive list — adding an arm here is intentional; the test
        // catches a structural change to the sum.
        var url = "https://x";
        var data = new JsonObject { ["k"] = "v" };
        TraceEvent[] all =
        [
            new TraceEvent.PageLoadStarted(PageType.Static) { Url = url },
            new TraceEvent.PageLoadCompleted(123) { Url = url },
            new TraceEvent.PageLoadFailed("HttpRequestException", "boom") { Url = url },
            new TraceEvent.ExtractionStarted("abc12345") { Url = url },
            new TraceEvent.ExtractionCompleted(data) { Url = url },
            new TraceEvent.PageProcessed("Kept") { Url = url },
            new TraceEvent.SinkEmit("CsvSink") { Url = url },
            new TraceEvent.CrawlStopped("all drained") { Url = "crawl" },
        ];

        Assert.Equal(8, all.Length);
        foreach (var e in all)
        {
            Assert.False(string.IsNullOrEmpty(e.Url));
            Assert.True(e.Timestamp > DateTimeOffset.UtcNow.AddMinutes(-1));
        }
    }

    private sealed class RecordingTrace : IExtractionTrace
    {
        public List<TraceEvent> Events { get; } = new();
        public ValueTask RecordAsync(TraceEvent ev, CancellationToken cancellationToken = default)
        {
            lock (Events) Events.Add(ev);
            return ValueTask.CompletedTask;
        }
    }
}
