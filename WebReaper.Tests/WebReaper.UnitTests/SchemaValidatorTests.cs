using System.Text.Json.Nodes;
using WebReaper.Core.Parser.Abstract;
using WebReaper.Core.Parser.Concrete;
using WebReaper.Domain.Parsing;

namespace WebReaper.UnitTests;

// ADR-0062: the ISchemaValidator seam and its default implementation
// SchemaSatisfiedValidator. Tests pin the ADR-0029-aligned per-element
// rules (string-empty triggers; integer 0 / boolean false do not),
// the Reason contents naming the failing field path(s), the
// strategy-local "no schema" handling, and the backward-compat static
// shim.
public class SchemaValidatorTests
{
    private static ISchemaValidator Validator => SchemaSatisfiedValidator.Instance;

    [Fact]
    public void Null_schema_is_trivially_valid()
    {
        var verdict = Validator.Validate(new JsonObject(), schema: null);
        Assert.True(verdict.IsValid);
        Assert.Null(verdict.Reason);
    }

    [Fact]
    public void Null_extracted_is_trivially_valid()
    {
        // The wrapper passes a null record only at edges (e.g. mid-
        // pipeline failure surfaced as None); the default treats it
        // as nothing-to-check.
        var schema = new Schema { new SchemaElement("title", "h1", DataType.String) };
        var verdict = Validator.Validate(null, schema);
        Assert.True(verdict.IsValid);
    }

    [Fact]
    public void Required_string_leaf_present_and_nonempty_is_valid()
    {
        var schema = new Schema { new SchemaElement("title", "h1", DataType.String) };
        var verdict = Validator.Validate(new JsonObject { ["title"] = "hello" }, schema);
        Assert.True(verdict.IsValid);
        Assert.Null(verdict.Reason);
    }

    [Fact]
    public void Required_string_leaf_empty_is_invalid_with_field_path_in_reason()
    {
        var schema = new Schema { new SchemaElement("title", "h1", DataType.String) };
        var verdict = Validator.Validate(new JsonObject { ["title"] = "" }, schema);
        Assert.False(verdict.IsValid);
        Assert.NotNull(verdict.Reason);
        Assert.Contains("title", verdict.Reason!);
        Assert.Contains("empty", verdict.Reason!);
    }

    [Fact]
    public void Required_string_leaf_missing_is_invalid_with_field_path_in_reason()
    {
        var schema = new Schema { new SchemaElement("title", "h1", DataType.String) };
        var verdict = Validator.Validate(new JsonObject(), schema);
        Assert.False(verdict.IsValid);
        Assert.NotNull(verdict.Reason);
        Assert.Contains("title", verdict.Reason!);
    }

    [Fact]
    public void Integer_zero_is_valid_data_not_a_missing_field()
    {
        // ADR-0029 alignment — only string-empty triggers; a legitimate
        // 0 is real data.
        var schema = new Schema { new SchemaElement("views", ".views", DataType.Integer) };
        var verdict = Validator.Validate(new JsonObject { ["views"] = 0 }, schema);
        Assert.True(verdict.IsValid);
    }

    [Fact]
    public void Boolean_false_is_valid_data_not_a_missing_field()
    {
        var schema = new Schema { new SchemaElement("active", ".active", DataType.Boolean) };
        var verdict = Validator.Validate(new JsonObject { ["active"] = false }, schema);
        Assert.True(verdict.IsValid);
    }

    [Fact]
    public void List_leaf_empty_is_invalid()
    {
        var schema = new Schema
        {
            new SchemaElement("tags", ".tag", DataType.String) { IsList = true }
        };
        var verdict = Validator.Validate(new JsonObject { ["tags"] = new JsonArray() }, schema);
        Assert.False(verdict.IsValid);
        Assert.NotNull(verdict.Reason);
        Assert.Contains("tags", verdict.Reason!);
    }

    [Fact]
    public void List_leaf_nonempty_is_valid()
    {
        var schema = new Schema
        {
            new SchemaElement("tags", ".tag", DataType.String) { IsList = true }
        };
        var verdict = Validator.Validate(new JsonObject { ["tags"] = new JsonArray("a") }, schema);
        Assert.True(verdict.IsValid);
    }

    [Fact]
    public void List_container_empty_is_invalid()
    {
        var schema = new Schema
        {
            Schema.ListOf("items", ".item",
                new SchemaElement("name", ".name", DataType.String))
        };
        var verdict = Validator.Validate(new JsonObject { ["items"] = new JsonArray() }, schema);
        Assert.False(verdict.IsValid);
        Assert.Contains("items", verdict.Reason!);
    }

    [Fact]
    public void List_container_nonempty_is_valid_without_descending_into_items()
    {
        // Default policy: presence + non-empty for the list container;
        // per-item field validity is not enforced (a stricter policy
        // would be a consumer-authored validator).
        var schema = new Schema
        {
            Schema.ListOf("items", ".item",
                new SchemaElement("name", ".name", DataType.String))
        };
        var verdict = Validator.Validate(
            new JsonObject
            {
                ["items"] = new JsonArray(new JsonObject { ["name"] = "" })
            },
            schema);
        Assert.True(verdict.IsValid);
    }

    [Fact]
    public void Multiple_failing_fields_are_all_named_in_reason()
    {
        // The walker collects every failure path — not just the first
        // — so the repairer / agent brain sees the full list.
        var schema = new Schema
        {
            new SchemaElement("title", "h1", DataType.String),
            new SchemaElement("price", ".price", DataType.String),
            new SchemaElement("description", ".desc", DataType.String)
        };
        var extracted = new JsonObject
        {
            ["title"] = "ok",
            ["price"] = "",
            ["description"] = ""
        };
        var verdict = Validator.Validate(extracted, schema);
        Assert.False(verdict.IsValid);
        Assert.NotNull(verdict.Reason);
        Assert.Contains("price", verdict.Reason!);
        Assert.Contains("description", verdict.Reason!);
        Assert.DoesNotContain("title", verdict.Reason!);
    }

    [Fact]
    public void Single_failure_uses_singular_reason_phrasing()
    {
        var schema = new Schema { new SchemaElement("price", ".price", DataType.String) };
        var verdict = Validator.Validate(new JsonObject { ["price"] = "" }, schema);
        Assert.False(verdict.IsValid);
        Assert.Equal("required field 'price' is empty", verdict.Reason);
    }

    [Fact]
    public void Instance_singleton_is_consistent()
    {
        // Stateless — multiple accesses yield the same instance, so
        // the builder can pass `_schemaValidator ?? Instance` cheaply.
        Assert.Same(SchemaSatisfiedValidator.Instance, SchemaSatisfiedValidator.Instance);
    }

    [Fact]
    public void ValidationResult_static_Valid_is_canonical()
    {
        var a = ValidationResult.Valid;
        var b = ValidationResult.Valid;
        Assert.Same(a, b);
        Assert.True(a.IsValid);
        Assert.Null(a.Reason);
    }

    [Fact]
    public void ValidationResult_Invalid_carries_reason()
    {
        var r = ValidationResult.Invalid("some reason");
        Assert.False(r.IsValid);
        Assert.Equal("some reason", r.Reason);
    }

    [Fact]
#pragma warning disable CS0618 // Type or member is obsolete — testing the obsolete shim
    public void Obsolete_static_IsSatisfied_still_works_and_returns_only_the_bool()
    {
        // Backward-compat: any external caller using the v10.0.x static
        // method still gets the right bool. The Reason is discarded by
        // the shim — that's the v11-removal trade-off.
        var schema = new Schema { new SchemaElement("title", "h1", DataType.String) };
        Assert.True(SchemaSatisfiedValidator.IsSatisfied(
            new JsonObject { ["title"] = "ok" }, schema));
        Assert.False(SchemaSatisfiedValidator.IsSatisfied(
            new JsonObject { ["title"] = "" }, schema));
        Assert.True(SchemaSatisfiedValidator.IsSatisfied(new JsonObject(), schema: null));
    }
#pragma warning restore CS0618
}
