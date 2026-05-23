using System.Text.Json.Nodes;
using WebReaper.Core.Parser.Abstract;
using WebReaper.Core.Parser.Concrete;
using WebReaper.Domain.Parsing;

namespace WebReaper.UnitTests;

// ADR-0046: the deterministic-first → fallback router. Tests pin the
// composition: primary served when valid, fallback served when not,
// default-validator behaviour over the full schema grammar, and the
// custom-predicate override.
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
    public async Task Custom_predicate_overrides_the_default_validator()
    {
        // A custom predicate that always says "invalid" forces a
        // fallback even though the default would say "valid".
        var router = new ExtractionRouter(
            primary: new StubExtractor(_ => new JsonObject { ["title"] = "primary value" }),
            fallback: new StubExtractor(_ => new JsonObject { ["title"] = "forced fallback" }),
            isValid: (_, _) => false);

        var schema = new Schema { new SchemaElement("title", "h1", DataType.String) };
        var result = await router.ExtractAsync("<doc/>", schema);

        Assert.Equal("forced fallback", result["title"]!.GetValue<string>());
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
    public void Schema_satisfied_validator_returns_true_for_null_schema()
    {
        // A null schema is trivially satisfied — the Markdown extractor
        // path has no schema to validate against, so a router wrapping
        // it must not always fall back.
        Assert.True(SchemaSatisfiedValidator.IsSatisfied(new JsonObject(), null));
    }

    private sealed class StubExtractor : IContentExtractor
    {
        private readonly Func<string, JsonObject> _emit;
        public StubExtractor(Func<string, JsonObject> emit) => _emit = emit;
        public Task<JsonObject> ExtractAsync(string document, Schema? schema)
            => Task.FromResult(_emit(document));
    }
}
