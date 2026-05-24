using WebReaper.Builders;
using WebReaper.Core;
using WebReaper.Domain.Parsing;

namespace WebReaper.UnitTests;

/// <summary>
/// ADR-0058: the engine-teardown disposal chain. <see cref="ScraperEngine"/>
/// is <see cref="IAsyncDisposable"/>; <see cref="DisposeAsync"/> walks
/// adapters in reverse warm-up order, then builder-registered teardown
/// hooks in LIFO order. Per-adapter disposal exceptions are swallowed.
/// </summary>
public class EngineTeardownDisposalTests
{
    [Fact]
    public async Task Build_then_dispose_invokes_OnTeardown_hooks_in_LIFO_order()
    {
        var order = new List<string>();
        var first = new RecordingDisposable("first", order);
        var second = new RecordingDisposable("second", order);
        var third = new RecordingDisposable("third", order);

        await using (var engine = await ScraperEngineBuilder
            .Crawl("https://example.com")
            .Extract(new Schema { new SchemaElement("title", "h1", DataType.String) })
            .OnTeardown(first)
            .OnTeardown(second)
            .OnTeardown(third)
            .BuildAsync())
        {
            // No RunAsync — we're testing disposal in isolation.
        }

        Assert.Equal(new[] { "third", "second", "first" }, order);
    }

    [Fact]
    public async Task Double_dispose_is_a_no_op()
    {
        var hook = new RecordingDisposable("hook", new List<string>());
        var engine = await ScraperEngineBuilder
            .Crawl("https://example.com")
            .Extract(new Schema { new SchemaElement("title", "h1", DataType.String) })
            .OnTeardown(hook)
            .BuildAsync();

        await engine.DisposeAsync();
        Assert.Equal(1, hook.CallCount);

        await engine.DisposeAsync();   // idempotent
        Assert.Equal(1, hook.CallCount);
    }

    [Fact]
    public async Task Hook_that_throws_is_logged_not_propagated()
    {
        var logger = new CapturingLogger();
        var bad = new ThrowingDisposable();
        var good = new RecordingDisposable("good", new List<string>());

        await using (var engine = await ScraperEngineBuilder
            .Crawl("https://example.com")
            .Extract(new Schema { new SchemaElement("title", "h1", DataType.String) })
            .WithLogger(logger)
            .OnTeardown(bad)
            .OnTeardown(good)
            .BuildAsync())
        {
            // Disposal at scope-exit runs both hooks; bad throws, good still runs.
        }

        Assert.Equal(1, good.CallCount);   // post-throw hook still ran
        Assert.Contains(logger.Entries, e =>
            e.Level == Microsoft.Extensions.Logging.LogLevel.Warning &&
            e.Message.Contains("Disposal of", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OnTeardown_null_throws_ArgumentNullException()
    {
        var builder = ScraperEngineBuilder.Crawl("https://example.com")
            .Extract(new Schema { new SchemaElement("title", "h1", DataType.String) });

        Assert.Throws<ArgumentNullException>(() => builder.OnTeardown(null!));
    }

    private sealed class RecordingDisposable : IAsyncDisposable
    {
        private readonly string _label;
        private readonly List<string> _order;
        public int CallCount { get; private set; }
        public RecordingDisposable(string label, List<string> order)
        {
            _label = label;
            _order = order;
        }
        public ValueTask DisposeAsync()
        {
            CallCount++;
            _order.Add(_label);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingDisposable : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => throw new InvalidOperationException("teardown burped");
    }
}
