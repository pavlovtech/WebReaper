using System.Text.Json.Nodes;
using WebReaper.AI;
using WebReaper.Domain.Parsing;

namespace WebReaper.AI.Tests;

// ADR-0044: the pure Schema → JSON Schema converter. Tests pin the
// shape exactly — leaf type mapping, list arms, nested objects,
// list-of-objects, the dropped-selector property.
public class SchemaJsonSchemaBridgeTests
{
    private static JsonObject Convert(Schema schema) =>
        SchemaJsonSchemaBridge.ToJsonSchema(schema);

    [Fact]
    public void Empty_schema_emits_object_with_no_properties()
    {
        var result = Convert(new Schema());

        Assert.Equal("object", result["type"]!.GetValue<string>());
        Assert.Empty(result["properties"]!.AsObject());
        Assert.Empty(result["required"]!.AsArray());
    }

    [Fact]
    public void Single_leaf_emits_typed_property()
    {
        var schema = new Schema
        {
            new SchemaElement("title", "h1", DataType.String)
        };

        var result = Convert(schema);
        var props = result["properties"]!.AsObject();

        Assert.Equal("string", props["title"]!["type"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(DataType.String, "string")]
    [InlineData(DataType.Integer, "integer")]
    [InlineData(DataType.Float, "number")]
    [InlineData(DataType.Boolean, "boolean")]
    [InlineData(DataType.DataTime, "string")]
    public void Leaf_types_map_to_json_schema_types(DataType from, string toJson)
    {
        var schema = new Schema
        {
            new SchemaElement("v", ".v", from)
        };

        var props = Convert(schema)["properties"]!.AsObject();
        Assert.Equal(toJson, props["v"]!["type"]!.GetValue<string>());
    }

    [Fact]
    public void Leaf_with_is_list_becomes_array_of_typed_items()
    {
        var schema = new Schema
        {
            new SchemaElement("tags", ".tag", DataType.String) { IsList = true }
        };

        var props = Convert(schema)["properties"]!.AsObject();
        var tags = props["tags"]!;

        Assert.Equal("array", tags["type"]!.GetValue<string>());
        Assert.Equal("string", tags["items"]!["type"]!.GetValue<string>());
    }

    [Fact]
    public void Nested_schema_becomes_nested_object()
    {
        var schema = new Schema
        {
            new Schema("post")
            {
                Children =
                {
                    new SchemaElement("title", "h1", DataType.String),
                    new SchemaElement("views", ".v", DataType.Integer)
                }
            }
        };

        var props = Convert(schema)["properties"]!.AsObject();
        var post = props["post"]!.AsObject();

        Assert.Equal("object", post["type"]!.GetValue<string>());
        var postProps = post["properties"]!.AsObject();
        Assert.Equal("string", postProps["title"]!["type"]!.GetValue<string>());
        Assert.Equal("integer", postProps["views"]!["type"]!.GetValue<string>());
    }

    [Fact]
    public void Nested_schema_with_is_list_becomes_array_of_objects()
    {
        // ADR-0028 requires a non-empty Selector for any list container —
        // the LLM extractor doesn't use the selector but the Schema
        // grammar still enforces it at construction.
        var schema = new Schema
        {
            Schema.ListOf("items", ".item",
                new SchemaElement("name", ".n", DataType.String),
                new SchemaElement("price", ".p", DataType.Float))
        };

        var props = Convert(schema)["properties"]!.AsObject();
        var items = props["items"]!.AsObject();

        Assert.Equal("array", items["type"]!.GetValue<string>());
        var itemsItems = items["items"]!.AsObject();
        Assert.Equal("object", itemsItems["type"]!.GetValue<string>());
        Assert.True(itemsItems["properties"]!.AsObject().ContainsKey("name"));
        Assert.True(itemsItems["properties"]!.AsObject().ContainsKey("price"));
    }

    [Fact]
    public void Required_array_contains_every_top_level_field()
    {
        var schema = new Schema
        {
            new SchemaElement("a", ".a", DataType.String),
            new SchemaElement("b", ".b", DataType.Integer)
        };

        var required = Convert(schema)["required"]!.AsArray()
            .Select(n => n!.GetValue<string>()).ToList();

        Assert.Equal(new[] { "a", "b" }, required);
    }

    [Fact]
    public void Selectors_are_dropped_from_the_output()
    {
        // The LLM extracts semantically — selectors don't survive the
        // bridge. This pins that intent.
        var schema = new Schema
        {
            new SchemaElement("title", "h1.special-selector", DataType.String)
        };

        var result = Convert(schema);
        var serialised = result.ToJsonString();

        Assert.DoesNotContain("h1.special-selector", serialised);
        Assert.DoesNotContain("selector", serialised);
    }
}
