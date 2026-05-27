using System.Text.Json.Nodes;
using AngleSharp.Dom;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WebReaper.Builders;
using WebReaper.Core.Parser.Abstract;
using WebReaper.Core.Parser.Concrete;
using WebReaper.Domain.Parsing;

namespace WebReaper.UnitTests;

// ADR-0072: ContentExtractorPipeline is the shared extractor + validator
// + wrapping-logic module both ScraperEngineBuilder and AgentEngineBuilder
// embed. These tests pin the inner contract:
//
//   - GetExtractorOrDefault returns a SchemaFold when no extractor is
//     registered; memoises after first call (??=); WithContentExtractor
//     replaces the cached value.
//   - WithFallbackExtractor wraps the current extractor with
//     ExtractionRouter; WithSelfHealing wraps with
//     SelfHealingContentExtractor; both read the current validator at
//     the call site.
//   - The Func<ILogger> getter is invoked at wrapper-construction time,
//     NOT at pipeline-construction time. This is the regression the
//     design specifically guards against — capturing logger by value
//     would silently break WithLogger-after-WithFallbackExtractor.
public class ContentExtractorPipelineTests
{
    // ---- GetExtractorOrDefault ---------------------------------------------

    [Fact]
    public void GetExtractorOrDefault_returns_a_SchemaFold_when_unset()
    {
        var pipeline = new ContentExtractorPipeline(() => NullLogger.Instance);

        var result = pipeline.GetExtractorOrDefault();

        Assert.IsType<SchemaFold<IParentNode>>(result);
    }

    [Fact]
    public void GetExtractorOrDefault_memoises_after_first_call()
    {
        // Pre-ADR-0072, SpiderBuilder.Build used `??=` to commit the
        // default on first use. The pipeline preserves that behaviour
        // so BuildAsync reading the pipeline multiple times (e.g. once
        // for LearnedSchemaContentExtractor's inner extractor and again
        // for the spider sync) gets the SAME default instance, not a
        // fresh allocation each time.
        var pipeline = new ContentExtractorPipeline(() => NullLogger.Instance);

        var first = pipeline.GetExtractorOrDefault();
        var second = pipeline.GetExtractorOrDefault();

        Assert.Same(first, second);
    }

    [Fact]
    public void GetExtractorOrDefault_commits_the_default_to_the_pipeline_state()
    {
        // Calling GetExtractorOrDefault on a fresh pipeline is observable
        // as a state mutation: Extractor was null before, non-null after.
        // Document this so a future "make it idempotent" refactor revisits
        // the BuildAsync memoisation invariant first.
        var pipeline = new ContentExtractorPipeline(() => NullLogger.Instance);
        Assert.Null(pipeline.Extractor);

        var resolved = pipeline.GetExtractorOrDefault();

        Assert.Same(resolved, pipeline.Extractor);
    }

    [Fact]
    public void GetExtractorOrDefault_returns_the_registered_extractor_when_set()
    {
        var custom = new StubContentExtractor();
        var pipeline = new ContentExtractorPipeline(() => NullLogger.Instance);
        pipeline.WithContentExtractor(custom);

        Assert.Same(custom, pipeline.GetExtractorOrDefault());
    }

    [Fact]
    public void WithContentExtractor_replaces_an_already_registered_extractor()
    {
        var first = new StubContentExtractor();
        var second = new StubContentExtractor();
        var pipeline = new ContentExtractorPipeline(() => NullLogger.Instance);

        pipeline.WithContentExtractor(first);
        pipeline.WithContentExtractor(second);

        Assert.Same(second, pipeline.Extractor);
    }

    // ---- WithFallbackExtractor + validator threading -----------------------

    [Fact]
    public void WithFallbackExtractor_wraps_the_current_extractor_in_ExtractionRouter()
    {
        var primary = new StubContentExtractor();
        var fallback = new StubContentExtractor();
        var pipeline = new ContentExtractorPipeline(() => NullLogger.Instance);
        pipeline.WithContentExtractor(primary);

        pipeline.WithFallbackExtractor(fallback);

        Assert.IsType<ExtractionRouter>(pipeline.Extractor);
    }

    [Fact]
    public void WithFallbackExtractor_with_no_prior_extractor_wraps_the_default_SchemaFold()
    {
        // "Currently registered or default": when no extractor is set,
        // WithFallbackExtractor still works by composing against a
        // default SchemaFold. Asserting the result is an ExtractionRouter
        // is enough — its internals are unit-tested in ExtractionRouter's
        // own test file (this test just pins the composition shape).
        var fallback = new StubContentExtractor();
        var pipeline = new ContentExtractorPipeline(() => NullLogger.Instance);

        pipeline.WithFallbackExtractor(fallback);

        Assert.IsType<ExtractionRouter>(pipeline.Extractor);
    }

    [Fact]
    public void WithSelfHealing_wraps_the_current_extractor_in_SelfHealingContentExtractor()
    {
        var primary = new StubContentExtractor();
        var repairer = new StubSelectorRepairer();
        var pipeline = new ContentExtractorPipeline(() => NullLogger.Instance);
        pipeline.WithContentExtractor(primary);

        pipeline.WithSelfHealing(repairer);

        Assert.IsType<SelfHealingContentExtractor>(pipeline.Extractor);
    }

    [Fact]
    public void WithSchemaValidator_state_is_visible_via_the_Validator_property()
    {
        var validator = new StubSchemaValidator();
        var pipeline = new ContentExtractorPipeline(() => NullLogger.Instance);

        pipeline.WithSchemaValidator(validator);

        Assert.Same(validator, pipeline.Validator);
    }

    // ---- Func<ILogger> capture semantics (the regression guard) -------------

    [Fact]
    public void Logger_getter_is_invoked_at_each_wrapper_construction_not_at_pipeline_construction()
    {
        // The reason ContentExtractorPipeline takes Func<ILogger> instead
        // of a captured ILogger value: the outer builder's Logger field
        // is mutable via WithLogger. Capturing by value at construction
        // would silently break the case where WithLogger is called AFTER
        // pipeline construction but BEFORE WithFallbackExtractor.
        //
        // Pin the contract by:
        //   1. Verifying the getter is NOT called at construction time.
        //   2. Mutating the logger after construction, then calling
        //      WithFallbackExtractor, and verifying the getter saw the
        //      NEW logger (not the construction-time one).
        int getterCallCount = 0;
        ILogger latestLogger = NullLogger.Instance;
        ILogger? lastReturned = null;

        var pipeline = new ContentExtractorPipeline(() =>
        {
            getterCallCount++;
            lastReturned = latestLogger;
            return latestLogger;
        });

        // Construction must NOT call the getter eagerly.
        Assert.Equal(0, getterCallCount);
        Assert.Null(lastReturned);

        // Mutate the underlying logger to a sentinel value.
        var sentinel = NullLogger.Instance; // we'll compare by reference
        var fakeLogger = (ILogger)new FakeLogger();
        latestLogger = fakeLogger;

        // WithFallbackExtractor must call the getter at composition time
        // — at least once, picking up the post-mutation logger.
        pipeline.WithFallbackExtractor(new StubContentExtractor());

        Assert.True(getterCallCount > 0,
            "WithFallbackExtractor must call the logger getter, not capture at construction.");
        Assert.Same(fakeLogger, lastReturned);
        Assert.NotSame(sentinel, lastReturned);
    }

    // ---- Constructor argument validation ------------------------------------

    [Fact]
    public void Constructor_rejects_null_logger_provider()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ContentExtractorPipeline(null!));
    }

    [Fact]
    public void With_methods_reject_null_arguments()
    {
        var pipeline = new ContentExtractorPipeline(() => NullLogger.Instance);
        Assert.Throws<ArgumentNullException>(() => pipeline.WithContentExtractor(null!));
        Assert.Throws<ArgumentNullException>(() => pipeline.WithFallbackExtractor(null!));
        Assert.Throws<ArgumentNullException>(() => pipeline.WithSelfHealing(null!));
        Assert.Throws<ArgumentNullException>(() => pipeline.WithSchemaValidator(null!));
    }

    // ---- Stubs --------------------------------------------------------------

    private sealed class StubContentExtractor : IContentExtractor
    {
        public Task<JsonObject> ExtractAsync(string document, Schema? schema) =>
            Task.FromResult(new JsonObject());
    }

    private sealed class StubSelectorRepairer : ISelectorRepairer
    {
        public Task<Schema?> RepairAsync(
            Schema original,
            string document,
            JsonObject failedResult,
            string? failureReason = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Schema?>(null);
    }

    private sealed class StubSchemaValidator : ISchemaValidator
    {
        public ValidationResult Validate(JsonObject? extracted, Schema? schema) =>
            ValidationResult.Valid;
    }

    // A non-NullLogger ILogger used as a distinguishable sentinel for
    // the regression-guard test — Assert.Same compares by reference.
    private sealed class FakeLogger : ILogger
    {
        IDisposable? ILogger.BeginScope<TState>(TState state) => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        { }
    }
}
