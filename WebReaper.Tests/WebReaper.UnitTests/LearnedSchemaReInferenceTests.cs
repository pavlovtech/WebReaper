using System.Text.Json.Nodes;
using WebReaper.Core.Parser.Abstract;
using WebReaper.Core.Parser.Concrete;
using WebReaper.Domain.Parsing;

namespace WebReaper.UnitTests;

// ADR-0069: validator-driven re-inference for LearnedSchemaContentExtractor.
// The wrapper consults the registered ISchemaValidator on every inner
// extractor output; N consecutive failures drop the cached schema and
// trigger re-inference on the next call. The cost cap caps re-inferences
// per instance.
//
// Defaults preserve ADR-0067 v1 trust-the-cache behaviour
// (reInferAfterFailures = 0); the satellite's LlmSchemaInferrerOptions
// flips the default to 3.
public class LearnedSchemaReInferenceTests
{
    [Fact]
    public async Task ReInferAfterFailures_zero_preserves_v1_trust_the_cache()
    {
        // The ADR-0067 v1 behaviour: even with a forcing-invalid
        // validator, the cache stays put because the trigger is
        // disabled.
        var inferrer = new SequencingInferrer(
            new Schema { new SchemaElement("title", "h1") });
        var inner = new EmptyResultExtractor();
        var validator = new ForceInvalidValidator("test failure");

        var wrapper = new LearnedSchemaContentExtractor(
            inferrer, inner, goal: null, logger: null,
            validator: validator,
            reInferAfterFailures: 0);

        for (var i = 0; i < 10; i++)
            await wrapper.ExtractAsync($"<page{i}/>", schema: null);

        Assert.Equal(1, inferrer.Calls);
        Assert.NotNull(wrapper.InferredSchema);
    }

    [Fact]
    public async Task ReInferAfterFailures_3_drops_cache_after_third_consecutive_failure()
    {
        var inferrer = new SequencingInferrer(
            new Schema { new SchemaElement("title", "h1") },
            new Schema { new SchemaElement("title", "h2") });    // 2nd inference
        var inner = new EmptyResultExtractor();
        var validator = new ForceInvalidValidator("required field empty");

        var wrapper = new LearnedSchemaContentExtractor(
            inferrer, inner, goal: null, logger: null,
            validator: validator,
            reInferAfterFailures: 3);

        // 3 failures → cache dropped on the 3rd; 4th call re-infers.
        await wrapper.ExtractAsync("<page1/>", null);
        await wrapper.ExtractAsync("<page2/>", null);
        Assert.Equal(1, inferrer.Calls);           // not yet
        await wrapper.ExtractAsync("<page3/>", null);  // triggers drop
        // Cache is dropped synchronously inside the wrapper's lock — the
        // next call must re-infer.
        await wrapper.ExtractAsync("<page4/>", null);

        Assert.Equal(2, inferrer.Calls);
        Assert.Equal(1, wrapper.ReInferencesUsed);
        Assert.NotNull(wrapper.InferredSchema);
        // The new cached schema is the second one the SequencingInferrer
        // yielded.
        var element = Assert.IsType<SchemaElement>(wrapper.InferredSchema!.Children[0]);
        Assert.Equal("h2", element.Selector);
    }

    [Fact]
    public async Task One_success_between_failures_resets_the_counter()
    {
        // Consecutive-failure semantics: any valid result resets;
        // outlier pages don't burn an LLM call.
        var inferrer = new SequencingInferrer(
            new Schema { new SchemaElement("title", "h1") },
            new Schema { new SchemaElement("title", "h2") });
        var inner = new EmptyResultExtractor();
        var validator = new ScriptedValidator(
            // F, F, S, F, F, F — only the LAST three are consecutive,
            // so re-inference fires on the 6th call.
            ValidationResult.Invalid("1"),
            ValidationResult.Invalid("2"),
            ValidationResult.Valid,
            ValidationResult.Invalid("4"),
            ValidationResult.Invalid("5"),
            ValidationResult.Invalid("6"));

        var wrapper = new LearnedSchemaContentExtractor(
            inferrer, inner, goal: null, logger: null,
            validator: validator,
            reInferAfterFailures: 3);

        for (var i = 0; i < 6; i++)
            await wrapper.ExtractAsync($"<page{i}/>", null);

        // 7th call triggers the re-inference (cache cleared on the 6th).
        await wrapper.ExtractAsync("<page7/>", null);
        Assert.Equal(2, inferrer.Calls);
    }

    [Fact]
    public async Task Cost_cap_keeps_stale_schema_after_second_would_trigger_event()
    {
        // With cap = 1, the second would-trigger event leaves the cache
        // in place. The wrapper logs a Warning (not asserted — production
        // behaviour) and the stale schema continues serving.
        var inferrer = new SequencingInferrer(
            new Schema { new SchemaElement("title", "h1") },
            new Schema { new SchemaElement("title", "h2") },
            new Schema { new SchemaElement("title", "h3") });
        var inner = new EmptyResultExtractor();
        var validator = new ForceInvalidValidator("always fails");

        var wrapper = new LearnedSchemaContentExtractor(
            inferrer, inner, goal: null, logger: null,
            validator: validator,
            reInferAfterFailures: 3,
            maxReInferencesPerInstance: 1);

        // First 3 failures → first re-inference (uses cap)
        for (var i = 0; i < 4; i++)
            await wrapper.ExtractAsync($"<page{i}/>", null);
        Assert.Equal(2, inferrer.Calls);            // re-inferred once
        Assert.Equal(1, wrapper.ReInferencesUsed);

        // Next 3 failures → cap-hit; cache stays
        for (var i = 0; i < 3; i++)
            await wrapper.ExtractAsync($"<page{4 + i}/>", null);

        Assert.Equal(2, inferrer.Calls);            // still 2 — cap honoured
        Assert.Equal(1, wrapper.ReInferencesUsed);  // still 1
        // Cached schema is the second one (the cap-hit kept it in place
        // rather than reverting to the first).
        var element = Assert.IsType<SchemaElement>(wrapper.InferredSchema!.Children[0]);
        Assert.Equal("h2", element.Selector);
    }

    [Fact]
    public async Task Default_validator_treats_empty_string_as_invalid()
    {
        // Smoke-check the integration with the default
        // SchemaSatisfiedValidator (ADR-0062) — empty string on a
        // required leaf triggers; consistent with the validator's own
        // tests, just verifies the wrapper consults it correctly.
        var inferrer = new SequencingInferrer(
            new Schema { new SchemaElement("title", "h1") },
            new Schema { new SchemaElement("title", "h2") });
        var inner = new ConstantResultExtractor(
            new JsonObject { ["title"] = "" });

        var wrapper = new LearnedSchemaContentExtractor(
            inferrer, inner, goal: null, logger: null,
            validator: null,                         // → default
            reInferAfterFailures: 2);

        await wrapper.ExtractAsync("<p1/>", null);
        await wrapper.ExtractAsync("<p2/>", null);   // triggers drop
        await wrapper.ExtractAsync("<p3/>", null);   // re-infer

        Assert.Equal(2, inferrer.Calls);
    }

    [Fact]
    public async Task Default_validator_treats_integer_zero_as_valid()
    {
        // Inverse smoke check — a legitimate 0 doesn't trigger.
        var inferrer = new SequencingInferrer(
            new Schema { new SchemaElement("views", ".views", DataType.Integer) });
        var inner = new ConstantResultExtractor(
            new JsonObject { ["views"] = 0 });

        var wrapper = new LearnedSchemaContentExtractor(
            inferrer, inner, goal: null, logger: null,
            validator: null,
            reInferAfterFailures: 2);

        for (var i = 0; i < 10; i++)
            await wrapper.ExtractAsync($"<p{i}/>", null);

        Assert.Equal(1, inferrer.Calls);
    }

    [Fact]
    public async Task Parallel_workers_under_threshold_trigger_at_most_one_re_inference()
    {
        // 16 parallel calls with threshold=3 + cap=1: only the first
        // re-inference fires; the rest see the cap-hit path.
        var inferrer = new SequencingInferrer(
            new Schema { new SchemaElement("title", "h1") },
            new Schema { new SchemaElement("title", "h2") },
            new Schema { new SchemaElement("title", "h3") });
        var inner = new EmptyResultExtractor();
        var validator = new ForceInvalidValidator("always");

        var wrapper = new LearnedSchemaContentExtractor(
            inferrer, inner, goal: null, logger: null,
            validator: validator,
            reInferAfterFailures: 3,
            maxReInferencesPerInstance: 1);

        var tasks = Enumerable.Range(0, 16)
            .Select(i => wrapper.ExtractAsync($"<p{i}/>", null))
            .ToArray();
        await Task.WhenAll(tasks);

        // At most one re-inference under the cap; the inferrer is
        // called either 1 (no threshold reached parallel) or 2 (first
        // wave triggered drop, second wave re-inferred).
        Assert.InRange(inferrer.Calls, 1, 2);
        Assert.InRange(wrapper.ReInferencesUsed, 0, 1);
    }

    [Fact]
    public async Task Re_inference_uses_the_original_goal()
    {
        var inferrer = new GoalCapturingInferrer(
            new Schema { new SchemaElement("title", "h1") },
            new Schema { new SchemaElement("title", "h2") });
        var inner = new EmptyResultExtractor();
        var validator = new ForceInvalidValidator("any");

        var wrapper = new LearnedSchemaContentExtractor(
            inferrer, inner, goal: "product details", logger: null,
            validator: validator,
            reInferAfterFailures: 2);

        await wrapper.ExtractAsync("<p1/>", null);
        await wrapper.ExtractAsync("<p2/>", null);
        await wrapper.ExtractAsync("<p3/>", null);

        // Both inference calls received the same goal.
        Assert.Equal(new[] { "product details", "product details" }, inferrer.Goals.ToArray());
    }

    [Fact]
    public void Constructor_rejects_negative_thresholds()
    {
        var inferrer = new SequencingInferrer(new Schema { new SchemaElement("t", "h1") });
        var inner = new EmptyResultExtractor();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new LearnedSchemaContentExtractor(
                inferrer, inner, null, null, null,
                reInferAfterFailures: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new LearnedSchemaContentExtractor(
                inferrer, inner, null, null, null,
                reInferAfterFailures: 1,
                maxReInferencesPerInstance: -1));
    }

    [Fact]
    public async Task Re_inferences_used_property_tracks_count()
    {
        // Threshold=3: every group of 3 consecutive failures triggers
        // one re-inference. After 9 always-failing calls with 3
        // schemas available, we observe re-inferences after the 3rd
        // and 6th call (re-inferences = 2 by the 9th).
        var inferrer = new SequencingInferrer(
            new Schema { new SchemaElement("t", "h1") },
            new Schema { new SchemaElement("t", "h2") },
            new Schema { new SchemaElement("t", "h3") });
        var inner = new EmptyResultExtractor();
        var validator = new ForceInvalidValidator("any");

        var wrapper = new LearnedSchemaContentExtractor(
            inferrer, inner, goal: null, logger: null,
            validator: validator,
            reInferAfterFailures: 3);

        Assert.Equal(0, wrapper.ReInferencesUsed);
        for (var i = 0; i < 3; i++) await wrapper.ExtractAsync($"<p{i}/>", null);
        Assert.Equal(1, wrapper.ReInferencesUsed);
        for (var i = 0; i < 3; i++) await wrapper.ExtractAsync($"<p{3 + i}/>", null);
        Assert.Equal(2, wrapper.ReInferencesUsed);
        // The counter is the "commitment to spend" semantic — incremented
        // at the drop, before the actual re-inference. After 6 always-
        // failing calls there are 2 drops; only 1 re-inference has
        // actually executed (the one between p3 and p4). A 7th call
        // executes the second re-inference, making inferrer.Calls catch
        // up to ReInferencesUsed + 1.
        await wrapper.ExtractAsync("<p6/>", null);
        Assert.Equal(wrapper.ReInferencesUsed + 1, inferrer.Calls);
    }

    // ---- stubs ----

    private sealed class SequencingInferrer : ISchemaInferrer
    {
        private readonly Schema[] _schemas;
        public int Calls { get; private set; }

        public SequencingInferrer(params Schema[] schemas) => _schemas = schemas;

        public Task<Schema> InferAsync(string document, string? goal = null,
            CancellationToken cancellationToken = default)
        {
            var idx = Math.Min(Calls, _schemas.Length - 1);
            Calls++;
            return Task.FromResult(_schemas[idx]);
        }
    }

    private sealed class GoalCapturingInferrer : ISchemaInferrer
    {
        private readonly Schema[] _schemas;
        public int Calls { get; private set; }
        public List<string?> Goals { get; } = new();

        public GoalCapturingInferrer(params Schema[] schemas) => _schemas = schemas;

        public Task<Schema> InferAsync(string document, string? goal = null,
            CancellationToken cancellationToken = default)
        {
            Goals.Add(goal);
            var idx = Math.Min(Calls, _schemas.Length - 1);
            Calls++;
            return Task.FromResult(_schemas[idx]);
        }
    }

    private sealed class EmptyResultExtractor : IContentExtractor
    {
        public Task<JsonObject> ExtractAsync(string document, Schema? schema)
            => Task.FromResult(new JsonObject());
    }

    private sealed class ConstantResultExtractor : IContentExtractor
    {
        private readonly JsonObject _result;
        public ConstantResultExtractor(JsonObject result) => _result = result;
        public Task<JsonObject> ExtractAsync(string document, Schema? schema)
            => Task.FromResult(JsonNode.Parse(_result.ToJsonString())!.AsObject());
    }

    private sealed class ForceInvalidValidator : ISchemaValidator
    {
        private readonly string _reason;
        public ForceInvalidValidator(string reason) => _reason = reason;
        public ValidationResult Validate(JsonObject? extracted, Schema? schema)
            => ValidationResult.Invalid(_reason);
    }

    private sealed class ScriptedValidator : ISchemaValidator
    {
        private readonly ValidationResult[] _verdicts;
        private int _calls;
        public ScriptedValidator(params ValidationResult[] verdicts) => _verdicts = verdicts;
        public ValidationResult Validate(JsonObject? extracted, Schema? schema)
        {
            var idx = Math.Min(_calls, _verdicts.Length - 1);
            _calls++;
            return _verdicts[idx];
        }
    }
}
