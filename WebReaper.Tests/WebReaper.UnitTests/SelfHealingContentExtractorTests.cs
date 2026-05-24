using System.Text.Json.Nodes;
using WebReaper.Core.Parser.Abstract;
using WebReaper.Core.Parser.Concrete;
using WebReaper.Domain.Parsing;

namespace WebReaper.UnitTests;

// ADR-0047: the self-healing wrapper. Tests pin the cache-then-validate
// loop: primary served when valid, repairer called on failure, patched
// schema validated, cache populated, subsequent calls served from cache.
//
// ADR-0062: the validator is now an ISchemaValidator seam. Tests cover
// the new constructor parameter (default vs custom), the failure-reason
// threading to the repairer, and force-valid / force-invalid validators
// as test sugar.
public class SelfHealingContentExtractorTests
{
    [Fact]
    public async Task Primary_succeeds_and_repairer_is_not_called()
    {
        var repairer = new CountingRepairer();
        var extractor = new SelfHealingContentExtractor(
            primary: new MapPrimary(_ => new JsonObject { ["title"] = "ok" }),
            repairer: repairer);

        var schema = new Schema { new SchemaElement("title", "h1", DataType.String) };
        var result = await extractor.ExtractAsync("<doc/>", schema);

        Assert.Equal("ok", result["title"]!.GetValue<string>());
        Assert.Equal(0, repairer.CallCount);
    }

    [Fact]
    public async Task Primary_fails_repairer_returns_null_then_failed_result_is_returned()
    {
        var extractor = new SelfHealingContentExtractor(
            primary: new MapPrimary(_ => new JsonObject { ["title"] = "" }),
            repairer: new NullRepairer());

        var schema = new Schema { new SchemaElement("title", "h1", DataType.String) };
        var result = await extractor.ExtractAsync("<doc/>", schema);

        Assert.Equal("", result["title"]!.GetValue<string>());
    }

    [Fact]
    public async Task Primary_fails_repairer_returns_patch_patch_validates_cache_is_populated()
    {
        var patched = new Schema { new SchemaElement("title", ".new-selector", DataType.String) };

        var primary = new MapPrimary(schema =>
        {
            // Trick: succeed only when given the patched schema.
            if (ReferenceEquals(schema, patched))
                return new JsonObject { ["title"] = "rescued" };
            return new JsonObject { ["title"] = "" };
        });

        var repairer = new CountingRepairer(returnSchema: patched);
        var extractor = new SelfHealingContentExtractor(primary, repairer);

        var original = new Schema { new SchemaElement("title", "h1", DataType.String) };

        // First call: primary fails, repairer runs, patch validates.
        var first = await extractor.ExtractAsync("<doc/>", original);
        Assert.Equal("rescued", first["title"]!.GetValue<string>());
        Assert.Equal(1, repairer.CallCount);

        // Second call: cache hit; repairer NOT called again.
        var second = await extractor.ExtractAsync("<doc/>", original);
        Assert.Equal("rescued", second["title"]!.GetValue<string>());
        Assert.Equal(1, repairer.CallCount);  // unchanged
    }

    [Fact]
    public async Task Patched_schema_that_still_fails_validation_is_not_cached()
    {
        // The repairer proposes a patch, but the patch also fails
        // validation. The wrapper returns the patched result (best-
        // effort) but does NOT cache — next call retries the repairer.
        var stillBroken = new Schema { new SchemaElement("title", ".still-broken", DataType.String) };
        var repairer = new CountingRepairer(returnSchema: stillBroken);

        var primary = new MapPrimary(_ => new JsonObject { ["title"] = "" });
        var extractor = new SelfHealingContentExtractor(primary, repairer);

        var original = new Schema { new SchemaElement("title", "h1", DataType.String) };

        await extractor.ExtractAsync("<doc/>", original);
        await extractor.ExtractAsync("<doc/>", original);

        // Repairer was called twice — no cache because the patch
        // didn't validate.
        Assert.Equal(2, repairer.CallCount);
    }

    [Fact]
    public async Task Null_schema_passes_through_to_primary()
    {
        // No schema → no validation → no repair. The Markdown extractor
        // path runs unchanged.
        var repairer = new CountingRepairer();
        var primary = new MapPrimary(_ => new JsonObject { ["markdown"] = "hello" });
        var extractor = new SelfHealingContentExtractor(primary, repairer);

        var result = await extractor.ExtractAsync("<doc/>", schema: null);

        Assert.Equal("hello", result["markdown"]!.GetValue<string>());
        Assert.Equal(0, repairer.CallCount);
    }

    [Fact]
    public void Constructor_rejects_null_primary_or_repairer()
    {
        Assert.Throws<ArgumentNullException>(() => new SelfHealingContentExtractor(null!, new NullRepairer()));
        Assert.Throws<ArgumentNullException>(() => new SelfHealingContentExtractor(new MapPrimary(_ => new JsonObject()), null!));
    }

    [Fact]
    public async Task Failure_reason_from_default_validator_is_threaded_to_the_repairer()
    {
        // ADR-0062 composition: the wrapper passes the validator's
        // Reason into the repairer so an LLM-backed repairer can put
        // it in the prompt. Pin that the reason names the failing field.
        var repairer = new CountingRepairer();
        var primary = new MapPrimary(_ => new JsonObject { ["title"] = "" });
        var extractor = new SelfHealingContentExtractor(primary, repairer);

        var schema = new Schema { new SchemaElement("title", "h1", DataType.String) };
        await extractor.ExtractAsync("<doc/>", schema);

        Assert.Equal(1, repairer.CallCount);
        Assert.NotNull(repairer.LastFailureReason);
        Assert.Contains("title", repairer.LastFailureReason!);
    }

    [Fact]
    public async Task Custom_validator_force_invalid_triggers_repair_even_when_default_would_pass()
    {
        // Inject a custom validator that always reports invalid. The
        // primary's output looks fine to the default, but the custom
        // validator forces the repair path — pinning that the wrapper
        // honours the injected seam, not the static default.
        var repairer = new CountingRepairer();
        var primary = new MapPrimary(_ => new JsonObject { ["title"] = "looks fine" });
        var extractor = new SelfHealingContentExtractor(
            primary, repairer, validator: new ForceInvalidValidator("custom reason"));

        var schema = new Schema { new SchemaElement("title", "h1", DataType.String) };
        await extractor.ExtractAsync("<doc/>", schema);

        Assert.Equal(1, repairer.CallCount);
        Assert.Equal("custom reason", repairer.LastFailureReason);
    }

    [Fact]
    public async Task Custom_validator_force_valid_suppresses_repair_even_when_default_would_fail()
    {
        // The inverse: a force-valid validator means the empty primary
        // result is treated as good — the repairer must NOT be called.
        var repairer = new CountingRepairer();
        var primary = new MapPrimary(_ => new JsonObject { ["title"] = "" });
        var extractor = new SelfHealingContentExtractor(
            primary, repairer, validator: new ForceValidValidator());

        var schema = new Schema { new SchemaElement("title", "h1", DataType.String) };
        var result = await extractor.ExtractAsync("<doc/>", schema);

        Assert.Equal("", result["title"]!.GetValue<string>());
        Assert.Equal(0, repairer.CallCount);
    }

    private sealed class MapPrimary : IContentExtractor
    {
        private readonly Func<Schema?, JsonObject> _map;
        public MapPrimary(Func<Schema?, JsonObject> map) => _map = map;
        public Task<JsonObject> ExtractAsync(string document, Schema? schema)
            => Task.FromResult(_map(schema));
    }

    private sealed class NullRepairer : ISelectorRepairer
    {
        public Task<Schema?> RepairAsync(
            Schema original,
            string document,
            JsonObject failedResult,
            string? failureReason = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<Schema?>(null);
    }

    private sealed class CountingRepairer : ISelectorRepairer
    {
        private readonly Schema? _returnSchema;
        public int CallCount { get; private set; }
        public string? LastFailureReason { get; private set; }

        public CountingRepairer(Schema? returnSchema = null) => _returnSchema = returnSchema;

        public Task<Schema?> RepairAsync(
            Schema original,
            string document,
            JsonObject failedResult,
            string? failureReason = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastFailureReason = failureReason;
            return Task.FromResult(_returnSchema);
        }
    }

    private sealed class ForceInvalidValidator : ISchemaValidator
    {
        private readonly string _reason;
        public ForceInvalidValidator(string reason) => _reason = reason;
        public ValidationResult Validate(JsonObject? extracted, Schema? schema)
            => ValidationResult.Invalid(_reason);
    }

    private sealed class ForceValidValidator : ISchemaValidator
    {
        public ValidationResult Validate(JsonObject? extracted, Schema? schema)
            => ValidationResult.Valid;
    }
}
