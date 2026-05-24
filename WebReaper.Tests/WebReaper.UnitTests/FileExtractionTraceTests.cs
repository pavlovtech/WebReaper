using System.Text.Json.Nodes;
using WebReaper.Core.Observability;
using WebReaper.Core.Observability.Concrete;
using WebReaper.Domain.Selectors;

namespace WebReaper.UnitTests;

/// <summary>
/// ADR-0018. Pins the JSONL line shape of FileExtractionTrace —
/// per-arm field coverage — and the buffered-drain crash-safety
/// (Dispose completes the queue so the consumer drains).
/// </summary>
public class FileExtractionTraceTests
{
    [Fact]
    public void Serialize_PageLoadStarted_carries_pageType_kind_ts_url()
    {
        var ev = new TraceEvent.PageLoadStarted(PageType.Dynamic) { Url = "https://x" };
        var line = FileExtractionTrace.Serialize(ev);
        var obj = JsonNode.Parse(line) as JsonObject;
        Assert.NotNull(obj);
        Assert.Equal("PageLoadStarted", obj!["kind"]!.GetValue<string>());
        Assert.Equal("https://x", obj["url"]!.GetValue<string>());
        Assert.Equal("Dynamic", obj["pageType"]!.GetValue<string>());
        Assert.NotNull(obj["ts"]);
    }

    [Fact]
    public void Serialize_PageLoadCompleted_carries_bytes()
    {
        var ev = new TraceEvent.PageLoadCompleted(1024) { Url = "https://x" };
        var obj = JsonNode.Parse(FileExtractionTrace.Serialize(ev)) as JsonObject;
        Assert.Equal(1024, obj!["bytes"]!.GetValue<int>());
    }

    [Fact]
    public void Serialize_PageLoadFailed_carries_exceptionType_and_message()
    {
        var ev = new TraceEvent.PageLoadFailed("HttpRequestException", "timeout") { Url = "https://x" };
        var obj = JsonNode.Parse(FileExtractionTrace.Serialize(ev)) as JsonObject;
        Assert.Equal("HttpRequestException", obj!["exceptionType"]!.GetValue<string>());
        Assert.Equal("timeout", obj["message"]!.GetValue<string>());
    }

    [Fact]
    public void Serialize_ExtractionStarted_carries_nullable_schemaHash()
    {
        var withHash = new TraceEvent.ExtractionStarted("deadbeef") { Url = "https://x" };
        var withoutHash = new TraceEvent.ExtractionStarted(null) { Url = "https://x" };

        var a = JsonNode.Parse(FileExtractionTrace.Serialize(withHash)) as JsonObject;
        var b = JsonNode.Parse(FileExtractionTrace.Serialize(withoutHash)) as JsonObject;

        Assert.Equal("deadbeef", a!["schemaHash"]!.GetValue<string>());
        // null serialises to a JSON null literal — present as a node but
        // GetValue<string?>() returns null.
        Assert.True(b!.ContainsKey("schemaHash"));
        Assert.Null(b["schemaHash"]);
    }

    [Fact]
    public void Serialize_ExtractionCompleted_carries_result_as_nested_object()
    {
        var data = new JsonObject { ["title"] = "Hello", ["views"] = 42 };
        var ev = new TraceEvent.ExtractionCompleted(data) { Url = "https://x" };
        var obj = JsonNode.Parse(FileExtractionTrace.Serialize(ev)) as JsonObject;
        var result = obj!["result"]!.AsObject();
        Assert.Equal("Hello", result["title"]!.GetValue<string>());
        Assert.Equal(42, result["views"]!.GetValue<int>());
    }

    [Fact]
    public void Serialize_PageProcessed_SinkEmit_CrawlStopped_each_carry_their_field()
    {
        var v = JsonNode.Parse(FileExtractionTrace.Serialize(
            new TraceEvent.PageProcessed("Kept") { Url = "https://x" })) as JsonObject;
        Assert.Equal("Kept", v!["verdict"]!.GetValue<string>());

        var s = JsonNode.Parse(FileExtractionTrace.Serialize(
            new TraceEvent.SinkEmit("JsonLinesFileSink") { Url = "https://x" })) as JsonObject;
        Assert.Equal("JsonLinesFileSink", s!["sinkName"]!.GetValue<string>());

        var c = JsonNode.Parse(FileExtractionTrace.Serialize(
            new TraceEvent.CrawlStopped("drained") { Url = "crawl" })) as JsonObject;
        Assert.Equal("drained", c!["reason"]!.GetValue<string>());
    }

    [Fact]
    public async Task End_to_end_writes_jsonl_to_disk()
    {
        var path = Path.GetTempFileName();
        try
        {
            using (var trace = new FileExtractionTrace(path))
            {
                await trace.RecordAsync(
                    new TraceEvent.PageLoadStarted(PageType.Static) { Url = "https://a" });
                await trace.RecordAsync(
                    new TraceEvent.PageLoadCompleted(100) { Url = "https://a" });
            }
            // Disposal completes the queue; give the consumer a moment to drain.
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
            string contents = "";
            while (DateTime.UtcNow < deadline)
            {
                contents = await File.ReadAllTextAsync(path);
                if (contents.Contains("PageLoadCompleted")) break;
                await Task.Delay(50);
            }

            var lines = contents.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.True(lines.Length >= 2, $"Expected 2 lines, got {lines.Length}: {contents}");
            Assert.Contains("PageLoadStarted", lines[0]);
            Assert.Contains("PageLoadCompleted", lines[1]);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void Construct_blank_path_throws()
    {
        Assert.ThrowsAny<ArgumentException>(() => new FileExtractionTrace(""));
        Assert.ThrowsAny<ArgumentException>(() => new FileExtractionTrace("   "));
    }
}
