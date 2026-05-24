using System.Text.Json.Nodes;
using WebReaper.Core.Parser.Abstract;
using WebReaper.Core.Parser.Concrete;
using WebReaper.Domain.Parsing;

namespace WebReaper.UnitTests;

// ADR-0046: the deterministic-first → fallback router. Tests pin the
// composition: primary served when valid, fallback served when not,
// default-validator behaviour over the full schema grammar.
//
// ADR-0062 update: the `Func<JsonObject, Schema?, bool>?` predicate
// was replaced by an `ISchemaValidator?`; the custom-predicate tests
// now swap a stub validator. Default-null still preserves behaviour
// via the singleton SchemaSatisfiedValidator.Instance.
public class ExtractionRouterTests
{
    [Fact]
    public async Task Primary_result_is_served_when_satisfied()
    {
        var router = new ExtractionRouter(
            primary: new StubExtractor(_ => new JsonObject { ["title"] = "from primary" }),
            fallback: new StubExtractor(_ => new JsonObject { ["title"] = "from fallback" }));

        var schema = new Schema { new SchemaElement("title", "h1", DataType.String) };
        var result = await router.ExtractAsync("<doc/>", schema);

        Assert.Equal("from primary", result["title"]!.GetValue<string>());
    }

    [Fact]
    public async Task Fallback_is_served_when_primary_returns_empty_string_for_required_leaf()
    {
        var router = new ExtractionRouter(
            primary: new StubExtractor(_ => new JsonObject { ["title"] = "" }),
            fallback: new StubExtractor(_ => new JsonObject { ["title"] = "rescued" }));

        var schema = new Schema { new SchemaElement("title", "h1", DataType.String) };
        var result = await router.ExtractAsync("<doc/>", schema);

        Assert.Equal("rescued", result["title"]!.GetValue<string>());
    }

    [Fact]
    public async Task Fallback_is_served_when_primary_omits_a_required_field()
    {
        var router = new ExtractionRouter(
            primary: new StubExtractor(_ => new JsonObject { /* no title */ }),
            fallback: new StubExtractor(_ => new JsonObject { ["title"] = "rescued" }));

        var schema = new Schema { new SchemaElement("title", "h1", DataType.String) };
        var result = await router.ExtractAsync("<doc/>", schema);

        Assert.Equal("rescued", result["title"]!.GetValue<string>());
    }

    [Fact]
    public async Task Custom_validator_can_force_fallback_even_when_default_would_pass()
    {
        // ADR-0062: a force-invalid validator means the primary result
        // is always treated as bad — the fallback is served.
        var router = new ExtractionRouter(
            primary: new StubExtractor(_ => new JsonObject { ["title"] = "primary value" }),
            fallback: new StubExtractor(_ => new JsonObject { ["title"] = "forced fallback" }),
            validator: new ForceInvalidValidator("custom reason"));

        var schema = new Schema { new SchemaElement("title", "h1", DataType.String) };
        var result = await router.ExtractAsync("<doc/>", schema);

        Assert.Equal("forced fallback", result["title"]!.GetValue<string>());
    }

    [Fact]
    public async Task Custom_validator_can_suppress_fallback_even_when_default_would_fail()
    {
        // The inverse: a force-valid validator means the empty primary
        // is treated as good — no escalation. Pinned for the test sugar
        // pattern: stub the validator to scope a router test to the
        // routing logic, not the validation logic.
        var router = new ExtractionRouter(
            primary: new StubExtractor(_ => new JsonObject { ["title"] = "" }),
            fallback: new StubExtractor(_ => new JsonObject { ["title"] = "should not run" }),
            validator: new ForceValidValidator());

        var schema = new Schema { new SchemaElement("title", "h1", DataType.String) };
        var result = await router.ExtractAsync("<doc/>", schema);

        Assert.Equal("", result["title"]!.GetValue<string>());
    }

    [Fact]
    public async Task Empty_list_field_triggers_fallback()
    {
        var router = new ExtractionRouter(
            primary: new StubExtractor(_ => new JsonObject { ["tags"] = new JsonArray() }),
            fallback: new StubExtractor(_ => new JsonObject
            {
                ["tags"] = new JsonArray("a", "b")
            }));

        var schema = new Schema
        {
            new SchemaElement("tags", ".tag", DataType.String) { IsList = true }
        };
        var result = await router.ExtractAsync("<doc/>", schema);

        Assert.Equal(2, result["tags"]!.AsArray().Count);
    }

    [Fact]
    public async Task Non_empty_list_field_does_not_trigger_fallback()
    {
        var router = new ExtractionRouter(
            primary: new StubExtractor(_ => new JsonObject
            {
                ["tags"] = new JsonArray("a")
            }),
            fallback: new StubExtractor(_ => new JsonObject
            {
                ["tags"] = new JsonArray("from fallback")
            }));

        var schema = new Schema
        {
            new SchemaElement("tags", ".tag", DataType.String) { IsList = true }
        };
        var result = await router.ExtractAsync("<doc/>", schema);

        Assert.Equal("a", result["tags"]![0]!.GetValue<string>());
    }

    [Fact]
    public async Task Integer_zero_is_a_valid_value_not_a_missing_field()
    {
        // A legitimate 0 / false must not trigger the fallback —
        // string-empty is the only "missing" marker for typed leaves.
        var router = new ExtractionRouter(
            primary: new StubExtractor(_ => new JsonObject { ["views"] = 0 }),
            fallback: new StubExtractor(_ => new JsonObject { ["views"] = 999 }));

        var schema = new Schema
        {
            new SchemaElement("views", ".views", DataType.Integer)
        };
        var result = await router.ExtractAsync("<doc/>", schema);

        Assert.Equal(0, result["views"]!.GetValue<int>());
    }

    [Fact]
    public void Constructor_rejects_null_primary_or_fallback()
    {
        var stub = new StubExtractor(_ => new JsonObject());
        Assert.Throws<ArgumentNullException>(() => new ExtractionRouter(null!, stub));
        Assert.Throws<ArgumentNullException>(() => new ExtractionRouter(stub, null!));
    }

    [Fact]
    public void Default_validator_returns_valid_for_null_schema()
    {
        // A null schema is trivially satisfied — the Markdown extractor
        // path has no schema to validate against, so a router wrapping
        // it must not always fall back.
        var verdict = SchemaSatisfiedValidator.Instance.Validate(new JsonObject(), null);
        Assert.True(verdict.IsValid);
        Assert.Null(verdict.Reason);
    }

    private sealed class StubExtractor : IContentExtractor
    {
        private readonly Func<string, JsonObject> _emit;
        public StubExtractor(Func<string, JsonObject> emit) => _emit = emit;
        public Task<JsonObject> ExtractAsync(string document, Schema? schema)
            => Task.FromResult(_emit(document));
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
