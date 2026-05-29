using System.Text.Json.Nodes;
using WebReaper.Infra.Abstract;
using WebReaper.Processing;
using WebReaper.Processing.Abstract;
using WebReaper.Sinks.Abstract;
using WebReaper.Sinks.Models;
using Xunit;

namespace WebReaper.UnitTests;

/// <summary>
/// ADR-0076: the Post-extraction pipeline is the one runtime home for the
/// Process (page-processor pipeline) + Emit (Sink fan-out) surface, plus the
/// warm-up and disposal of the held sinks and processors. These tests pin that
/// contract directly at the module's interface — the test surface the two
/// drivers previously forced through their heavyweight end-to-end paths.
/// </summary>
public class PostExtractionPipelineTests
{
    private static ParsedData Record(string url = "https://example.com/p", string key = "k", string value = "v")
        => new(url, new JsonObject { [key] = value });

    // ---- Fakes --------------------------------------------------------------

    private sealed class CollectingSink : IScraperSink
    {
        public List<ParsedData> Emitted { get; } = new();
        public bool DataCleanupOnStart { get; set; }
        public Task EmitAsync(ParsedData entity, CancellationToken ct = default)
        {
            Emitted.Add(entity);
            return Task.CompletedTask;
        }
    }

    // Mutates the Data it receives — proves the per-sink deep-clone isolates sinks.
    private sealed class MutatingSink : IScraperSink
    {
        private readonly string _tag;
        public ParsedData? Received;
        public bool DataCleanupOnStart { get; set; }
        public MutatingSink(string tag) => _tag = tag;
        public Task EmitAsync(ParsedData entity, CancellationToken ct = default)
        {
            entity.Data["mutated"] = _tag;
            Received = entity;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeProcessor : IPageProcessor
    {
        private readonly Func<PageContext, PageVerdict> _fn;
        public List<PageContext> Seen { get; } = new();
        public FakeProcessor(Func<PageContext, PageVerdict> fn) => _fn = fn;
        public ValueTask<PageVerdict> ProcessAsync(PageContext context, CancellationToken ct)
        {
            Seen.Add(context);
            return ValueTask.FromResult(_fn(context));
        }
    }

    private sealed class LifecycleSink : IScraperSink, IAsyncInitializable, IAsyncDisposable
    {
        public int Inits;
        public int Disposes;
        public bool DataCleanupOnStart { get; set; }
        public Task InitializeAsync() { Inits++; return Task.CompletedTask; }
        public Task EmitAsync(ParsedData entity, CancellationToken ct = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() { Disposes++; return ValueTask.CompletedTask; }
    }

    // Records its name into a shared list on dispose — pins disposal order.
    private sealed class OrderSink : IScraperSink, IAsyncDisposable
    {
        private readonly List<string> _log;
        private readonly string _name;
        public bool DataCleanupOnStart { get; set; }
        public OrderSink(List<string> log, string name) { _log = log; _name = name; }
        public Task EmitAsync(ParsedData entity, CancellationToken ct = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() { _log.Add(_name); return ValueTask.CompletedTask; }
    }

    private sealed class OrderProcessor : IPageProcessor, IAsyncDisposable
    {
        private readonly List<string> _log;
        private readonly string _name;
        public OrderProcessor(List<string> log, string name) { _log = log; _name = name; }
        public ValueTask<PageVerdict> ProcessAsync(PageContext context, CancellationToken ct)
            => ValueTask.FromResult(PageVerdict.Keep(context.Data));
        public ValueTask DisposeAsync() { _log.Add(_name); return ValueTask.CompletedTask; }
    }

    private sealed class ThrowingOnDisposeSink : IScraperSink, IAsyncDisposable
    {
        public bool DataCleanupOnStart { get; set; }
        public Task EmitAsync(ParsedData entity, CancellationToken ct = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => throw new InvalidOperationException("boom");
    }

    // ---- ProcessAndEmit: the fused path -------------------------------------

    [Fact]
    public async Task No_processors_no_sinks_returns_the_record()
    {
        var pipeline = new PostExtractionPipeline(Array.Empty<IScraperSink>());
        var record = Record();

        var result = await pipeline.ProcessAndEmitAsync(record, "<html>", Array.Empty<string>(), null);

        Assert.Same(record, result);
    }

    [Fact]
    public async Task Record_is_emitted_to_every_sink()
    {
        var a = new CollectingSink();
        var b = new CollectingSink();
        var pipeline = new PostExtractionPipeline(new IScraperSink[] { a, b });

        await pipeline.ProcessAndEmitAsync(Record(), "<html>", Array.Empty<string>(), null);

        Assert.Single(a.Emitted);
        Assert.Single(b.Emitted);
    }

    [Fact]
    public async Task Processors_run_in_order_each_seeing_the_previous_record()
    {
        // p1 replaces the record with one carrying "stage"="1"; p2 must see it.
        var p1 = new FakeProcessor(ctx =>
        {
            var data = (JsonObject)ctx.Data.Data.DeepClone();
            data["stage"] = "1";
            return PageVerdict.Keep(ctx.Data with { Data = data });
        });
        var p2 = new FakeProcessor(ctx => PageVerdict.Keep(ctx.Data));
        var sink = new CollectingSink();
        var pipeline = new PostExtractionPipeline(new IScraperSink[] { sink }, new IPageProcessor[] { p1, p2 });

        var result = await pipeline.ProcessAndEmitAsync(Record(), "<html>", Array.Empty<string>(), null);

        // p2 saw the record p1 produced (stage=1), and the survivor carries it.
        Assert.Equal("1", p2.Seen[0].Data.Data["stage"]?.GetValue<string>());
        Assert.Equal("1", result!.Data["stage"]?.GetValue<string>());
        Assert.Equal("1", sink.Emitted[0].Data["stage"]?.GetValue<string>());
    }

    [Fact]
    public async Task Drop_verdict_filters_the_page_no_sink_emits_and_returns_null()
    {
        var dropper = new FakeProcessor(_ => PageVerdict.Drop("not wanted"));
        var sink = new CollectingSink();
        var pipeline = new PostExtractionPipeline(new IScraperSink[] { sink }, new IPageProcessor[] { dropper });

        var result = await pipeline.ProcessAndEmitAsync(Record(), "<html>", Array.Empty<string>(), null);

        Assert.Null(result);
        Assert.Empty(sink.Emitted);
    }

    [Fact]
    public async Task A_throwing_processor_drops_the_page_without_aborting()
    {
        var thrower = new FakeProcessor(_ => throw new InvalidOperationException("processor bug"));
        var sink = new CollectingSink();
        var pipeline = new PostExtractionPipeline(new IScraperSink[] { sink }, new IPageProcessor[] { thrower });

        var result = await pipeline.ProcessAndEmitAsync(Record(), "<html>", Array.Empty<string>(), null);

        Assert.Null(result);
        Assert.Empty(sink.Emitted);
    }

    [Fact]
    public async Task A_cancelling_processor_propagates_OperationCanceledException()
    {
        var canceller = new FakeProcessor(_ => throw new OperationCanceledException());
        var pipeline = new PostExtractionPipeline(Array.Empty<IScraperSink>(), new IPageProcessor[] { canceller });

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            pipeline.ProcessAndEmitAsync(Record(), "<html>", Array.Empty<string>(), null));
    }

    [Fact]
    public async Task Each_sink_receives_its_own_clone_of_the_data()
    {
        var a = new MutatingSink("A");
        var b = new MutatingSink("B");
        var pipeline = new PostExtractionPipeline(new IScraperSink[] { a, b });
        var record = Record();

        var result = await pipeline.ProcessAndEmitAsync(record, "<html>", Array.Empty<string>(), null);

        // Each sink mutated only its own clone.
        Assert.Equal("A", a.Received!.Data["mutated"]?.GetValue<string>());
        Assert.Equal("B", b.Received!.Data["mutated"]?.GetValue<string>());
        // The returned survivor and the caller's original are untouched.
        Assert.Null(result!.Data["mutated"]);
        Assert.Null(record.Data["mutated"]);
    }

    // ---- Warm-up ------------------------------------------------------------

    [Fact]
    public async Task InitializeAsync_warms_capable_sinks_once_and_is_idempotent()
    {
        var sink = new LifecycleSink();
        var pipeline = new PostExtractionPipeline(new IScraperSink[] { sink });

        await pipeline.InitializeAsync();
        await pipeline.InitializeAsync();

        Assert.Equal(1, sink.Inits);
    }

    // ---- Disposal -----------------------------------------------------------

    [Fact]
    public async Task DisposeAsync_disposes_processors_then_sinks_each_in_reverse_order()
    {
        var log = new List<string>();
        var pipeline = new PostExtractionPipeline(
            new IScraperSink[] { new OrderSink(log, "sinkA"), new OrderSink(log, "sinkB") },
            new IPageProcessor[] { new OrderProcessor(log, "procA"), new OrderProcessor(log, "procB") });

        await pipeline.DisposeAsync();

        // Processors first (reverse), then sinks (reverse): procB, procA, sinkB, sinkA.
        Assert.Equal(new[] { "procB", "procA", "sinkB", "sinkA" }, log);
    }

    [Fact]
    public async Task DisposeAsync_swallows_per_adapter_exceptions_and_disposes_the_rest()
    {
        var survivor = new LifecycleSink();
        var pipeline = new PostExtractionPipeline(
            new IScraperSink[] { survivor, new ThrowingOnDisposeSink() });

        // Does not throw despite the sink whose DisposeAsync throws.
        await pipeline.DisposeAsync();

        Assert.Equal(1, survivor.Disposes);
    }

    [Fact]
    public async Task DisposeAsync_is_idempotent()
    {
        var sink = new LifecycleSink();
        var pipeline = new PostExtractionPipeline(new IScraperSink[] { sink });

        await pipeline.DisposeAsync();
        await pipeline.DisposeAsync();

        Assert.Equal(1, sink.Disposes);
    }
}
