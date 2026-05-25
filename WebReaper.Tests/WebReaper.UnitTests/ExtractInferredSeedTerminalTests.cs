using System.Text.Json.Nodes;
using WebReaper.Builders;
using WebReaper.Core.Parser.Abstract;
using WebReaper.Core.Parser.Concrete;
using WebReaper.Domain.Parsing;

namespace WebReaper.UnitTests;

// ADR-0067: builder-level integration tests for the third seed terminal.
// ExtractInferred(goal?) marks the builder for runtime schema inference;
// BuildAsync resolves the marker against the registered ISchemaInferrer
// and wraps the current content extractor with LearnedSchemaContentExtractor.
//
// The wrapper's runtime behaviour (first-call infers, parallel guard,
// goal routing to the inferrer) is pinned by LearnedSchemaContentExtractorTests.
// These tests cover the builder seam alone.
public class ExtractInferredSeedTerminalTests
{
    [Fact]
    public void ExtractInferred_returns_the_builder_for_chaining()
    {
        var builder = ScraperEngineBuilder
            .Crawl("https://example.com")
            .ExtractInferred();

        Assert.NotNull(builder);
        Assert.IsType<ScraperEngineBuilder>(builder);
    }

    [Fact]
    public void ExtractInferred_marks_the_builder()
    {
        var builder = (ScraperEngineBuilder)ScraperEngineBuilder
            .Crawl("https://example.com")
            .ExtractInferred();

        var (marked, goal) = builder.InferenceMarkerForTests;
        Assert.True(marked);
        Assert.Null(goal);
    }

    [Fact]
    public void ExtractInferred_with_goal_captures_the_goal_on_the_marker()
    {
        var builder = (ScraperEngineBuilder)ScraperEngineBuilder
            .Crawl("https://example.com")
            .ExtractInferred(goal: "product details");

        var (marked, goal) = builder.InferenceMarkerForTests;
        Assert.True(marked);
        Assert.Equal("product details", goal);
    }

    [Fact]
    public void Extract_with_schema_does_NOT_mark_the_builder_for_inference()
    {
        // Sanity — only the ExtractInferred terminal sets the marker.
        var builder = (ScraperEngineBuilder)ScraperEngineBuilder
            .Crawl("https://example.com")
            .Extract(new Schema { new SchemaElement("title", "h1") });

        var (marked, _) = builder.InferenceMarkerForTests;
        Assert.False(marked);
    }

    [Fact]
    public void AsMarkdown_does_NOT_mark_the_builder_for_inference()
    {
        var builder = (ScraperEngineBuilder)ScraperEngineBuilder
            .Crawl("https://example.com")
            .AsMarkdown();

        var (marked, _) = builder.InferenceMarkerForTests;
        Assert.False(marked);
    }

    [Fact]
    public async Task BuildAsync_throws_when_no_inferrer_registered()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ScraperEngineBuilder
                .Crawl("https://example.com")
                .ExtractInferred()
                .BuildAsync());

        Assert.Contains(".ExtractInferred", ex.Message);
        Assert.Contains("ISchemaInferrer", ex.Message);
        Assert.Contains("WithLlmSchemaInferrer", ex.Message);
        Assert.Contains("WithSchemaInferrer", ex.Message);
    }

    [Fact]
    public async Task BuildAsync_succeeds_when_inferrer_registered_via_WithSchemaInferrer()
    {
        var stub = new RecordingInferrer(new Schema
        {
            new SchemaElement("title", "h1")
        });

        var engine = await ScraperEngineBuilder
            .Crawl("https://example.com")
            .ExtractInferred()
            .WithSchemaInferrer(stub)
            .BuildAsync();

        Assert.NotNull(engine);
        await engine.DisposeAsync();
    }

    [Fact]
    public void WithSchemaInferrer_registers_the_inferrer_on_the_builder()
    {
        var stub = new RecordingInferrer(new Schema
        {
            new SchemaElement("title", "h1")
        });

        var builder = (ScraperEngineBuilder)ScraperEngineBuilder
            .Crawl("https://example.com")
            .ExtractInferred()
            .WithSchemaInferrer(stub);

        Assert.Same(stub, builder.SchemaInferrerForTests);
    }

    [Fact]
    public void Default_inferrer_is_the_null_sentinel()
    {
        var builder = (ScraperEngineBuilder)ScraperEngineBuilder
            .Crawl("https://example.com")
            .ExtractInferred();

        Assert.Same(NullSchemaInferrer.Instance, builder.SchemaInferrerForTests);
    }

    [Fact]
    public void WithSchemaInferrer_rejects_null()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ScraperEngineBuilder
                .Crawl("https://example.com")
                .ExtractInferred()
                .WithSchemaInferrer(null!));
    }

    [Fact]
    public async Task BuildAsync_does_not_throw_when_inferrer_registered_but_unused()
    {
        // .WithSchemaInferrer is silently ignored when the consumer chose
        // Extract(schema) or AsMarkdown() instead of ExtractInferred().
        // No throw, no warning.
        var stub = new RecordingInferrer(new Schema
        {
            new SchemaElement("title", "h1")
        });

        var engine = await ScraperEngineBuilder
            .Crawl("https://example.com")
            .AsMarkdown()
            .WithSchemaInferrer(stub)
            .BuildAsync();

        await engine.DisposeAsync();

        // The stub should never have been invoked — the wrapper is only
        // composed when ExtractInferred was the chosen terminal.
        Assert.Equal(0, stub.Calls);
    }

    private sealed class RecordingInferrer : ISchemaInferrer
    {
        private readonly Schema _schema;
        public int Calls { get; private set; }
        public string? LastGoal { get; private set; }

        public RecordingInferrer(Schema schema) => _schema = schema;

        public Task<Schema> InferAsync(string document, string? goal = null,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            LastGoal = goal;
            return Task.FromResult(_schema);
        }
    }
}
