using System.Text.Json.Nodes;
using WebReaper.Core.Parser.Abstract;
using WebReaper.Core.Parser.Concrete;
using WebReaper.Domain.Parsing;

namespace WebReaper.UnitTests;

// ADR-0067: contract tests for the LearnedSchemaContentExtractor wrapper.
// The wrapper composes an ISchemaInferrer with an inner IContentExtractor
// (typically SchemaFold) and caches the inferred schema per-instance.
// First page pays the inferrer; every subsequent page runs the inner
// extractor with the cached schema.
public class LearnedSchemaContentExtractorTests
{
    [Fact]
    public async Task First_call_invokes_inferrer_then_caches_result()
    {
        var inferrer = new CountingInferrer(new Schema
        {
            new SchemaElement("title", "h1")
        });
        var inner = new CapturingInner();

        var wrapper = new LearnedSchemaContentExtractor(inferrer, inner);

        await wrapper.ExtractAsync("<page1/>", schema: null);
        await wrapper.ExtractAsync("<page2/>", schema: null);
        await wrapper.ExtractAsync("<page3/>", schema: null);

        Assert.Equal(1, inferrer.Calls);
        Assert.Equal(3, inner.Calls.Count);
    }

    [Fact]
    public async Task Inferred_schema_property_is_null_before_first_call_and_populated_after()
    {
        var inferrer = new CountingInferrer(new Schema
        {
            new SchemaElement("title", "h1")
        });
        var wrapper = new LearnedSchemaContentExtractor(inferrer, new CapturingInner());

        Assert.Null(wrapper.InferredSchema);

        await wrapper.ExtractAsync("<page/>", schema: null);

        Assert.NotNull(wrapper.InferredSchema);
        Assert.Single(wrapper.InferredSchema!.Children);
    }

    [Fact]
    public async Task Inner_extractor_receives_inferred_schema_not_the_passed_argument()
    {
        // ADR-0067: the consumer chose ExtractInferred precisely because
        // they didn't supply a schema. The wrapper ignores the passed-in
        // null and feeds the inner extractor the inferred schema.
        var inferred = new Schema { new SchemaElement("title", "h1") };
        var inferrer = new CountingInferrer(inferred);
        var inner = new CapturingInner();

        var wrapper = new LearnedSchemaContentExtractor(inferrer, inner);
        await wrapper.ExtractAsync("<page/>", schema: null);

        Assert.Single(inner.Calls);
        Assert.Same(inferred, inner.Calls[0].Schema);
    }

    [Fact]
    public async Task Inner_extractor_sees_inferred_schema_even_when_caller_passes_one()
    {
        // The ExtractInferred path never has a real Schema at the call
        // site, but the seam permits one — the wrapper must still ignore
        // it (the consumer's choice was "let the inferrer decide").
        var inferred = new Schema { new SchemaElement("title", "h1") };
        var caller = new Schema { new SchemaElement("ignored", ".x") };
        var inferrer = new CountingInferrer(inferred);
        var inner = new CapturingInner();

        var wrapper = new LearnedSchemaContentExtractor(inferrer, inner);
        await wrapper.ExtractAsync("<page/>", caller);

        Assert.Same(inferred, inner.Calls[0].Schema);
        Assert.NotSame(caller, inner.Calls[0].Schema);
    }

    [Fact]
    public async Task Parallel_first_page_workers_all_see_one_inference()
    {
        // ADR-0067: Parallel.ForEachAsync may seed multiple workers at
        // once. The SemaphoreSlim-guarded double-checked locking must
        // ensure the inferrer is called exactly once.
        var inferrer = new SlowCountingInferrer(
            new Schema { new SchemaElement("title", "h1") },
            delay: TimeSpan.FromMilliseconds(50));
        var inner = new CapturingInner();

        var wrapper = new LearnedSchemaContentExtractor(inferrer, inner);

        // 16 parallel calls — well above any reasonable Parallel degree.
        var tasks = Enumerable.Range(0, 16)
            .Select(i => wrapper.ExtractAsync($"<page{i}/>", schema: null))
            .ToArray();
        await Task.WhenAll(tasks);

        Assert.Equal(1, inferrer.Calls);
        Assert.Equal(16, inner.Calls.Count);
    }

    [Fact]
    public async Task Goal_threads_through_to_inferrer()
    {
        var inferrer = new CountingInferrer(new Schema
        {
            new SchemaElement("title", "h1")
        });

        var wrapper = new LearnedSchemaContentExtractor(
            inferrer, new CapturingInner(), goal: "product details");

        await wrapper.ExtractAsync("<page/>", schema: null);

        Assert.Equal("product details", inferrer.LastGoal);
    }

    [Fact]
    public async Task Null_goal_threads_through_as_null()
    {
        var inferrer = new CountingInferrer(new Schema
        {
            new SchemaElement("title", "h1")
        });

        var wrapper = new LearnedSchemaContentExtractor(
            inferrer, new CapturingInner(), goal: null);

        await wrapper.ExtractAsync("<page/>", schema: null);

        Assert.Null(inferrer.LastGoal);
    }

    [Fact]
    public async Task NullSchemaInferrer_sentinel_throws_with_actionable_message()
    {
        // Defence-in-depth: the builder normally catches this at BuildAsync
        // time, but constructing the wrapper directly with the sentinel
        // surfaces the same error here.
        var wrapper = new LearnedSchemaContentExtractor(
            NullSchemaInferrer.Instance, new CapturingInner());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => wrapper.ExtractAsync("<page/>", null));
        Assert.Contains("ISchemaInferrer", ex.Message);
        Assert.Contains("WithLlmSchemaInferrer", ex.Message);
    }

    [Fact]
    public async Task Inferrer_returning_null_surfaces_actionable_throw()
    {
        var wrapper = new LearnedSchemaContentExtractor(
            new NullReturningInferrer(), new CapturingInner());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => wrapper.ExtractAsync("<page/>", null));
        Assert.Contains("returned null", ex.Message);
    }

    [Fact]
    public async Task Inferrer_throw_propagates_and_leaves_cache_unset()
    {
        var inferrer = new ThrowingInferrer(new InvalidOperationException("nope"));
        var inner = new CapturingInner();
        var wrapper = new LearnedSchemaContentExtractor(inferrer, inner);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => wrapper.ExtractAsync("<page/>", null));

        // Cache stays unset; the next call retries the inferrer.
        Assert.Null(wrapper.InferredSchema);
        Assert.Empty(inner.Calls);
    }

    [Fact]
    public async Task DisposeAsync_is_idempotent()
    {
        var wrapper = new LearnedSchemaContentExtractor(
            new CountingInferrer(new Schema { new SchemaElement("title", "h1") }),
            new CapturingInner());

        // ADR-0058's teardown chain swallows disposal exceptions, but a
        // wrapper that double-disposes its SemaphoreSlim would still log
        // a warning per call. Idempotent dispose keeps teardown quiet.
        await wrapper.DisposeAsync();
        await wrapper.DisposeAsync();
        await wrapper.DisposeAsync();
    }

    [Fact]
    public void Constructor_rejects_null_inferrer_or_inner()
    {
        var inner = new CapturingInner();
        var inferrer = new CountingInferrer(new Schema
        {
            new SchemaElement("title", "h1")
        });

        Assert.Throws<ArgumentNullException>(
            () => new LearnedSchemaContentExtractor(null!, inner));
        Assert.Throws<ArgumentNullException>(
            () => new LearnedSchemaContentExtractor(inferrer, null!));
    }

    private sealed class CountingInferrer : ISchemaInferrer
    {
        private readonly Schema _schema;
        public int Calls { get; private set; }
        public string? LastGoal { get; private set; }
        public string? LastDocument { get; private set; }

        public CountingInferrer(Schema schema) => _schema = schema;

        public Task<Schema> InferAsync(string document, string? goal = null,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            LastGoal = goal;
            LastDocument = document;
            return Task.FromResult(_schema);
        }
    }

    private sealed class SlowCountingInferrer : ISchemaInferrer
    {
        private readonly Schema _schema;
        private readonly TimeSpan _delay;
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);

        public SlowCountingInferrer(Schema schema, TimeSpan delay)
        {
            _schema = schema;
            _delay = delay;
        }

        public async Task<Schema> InferAsync(string document, string? goal = null,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            await Task.Delay(_delay, cancellationToken);
            return _schema;
        }
    }

    private sealed class NullReturningInferrer : ISchemaInferrer
    {
        public Task<Schema> InferAsync(string document, string? goal = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<Schema>(null!);
    }

    private sealed class ThrowingInferrer : ISchemaInferrer
    {
        private readonly Exception _exception;
        public ThrowingInferrer(Exception exception) => _exception = exception;
        public Task<Schema> InferAsync(string document, string? goal = null,
            CancellationToken cancellationToken = default)
            => throw _exception;
    }

    private sealed class CapturingInner : IContentExtractor
    {
        public List<(string Document, Schema? Schema)> Calls { get; } = new();

        public Task<JsonObject> ExtractAsync(string document, Schema? schema)
        {
            lock (Calls) Calls.Add((document, schema));
            return Task.FromResult(new JsonObject { ["captured"] = document });
        }
    }
}
