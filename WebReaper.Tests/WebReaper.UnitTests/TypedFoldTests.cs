using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using WebReaper.Core.Parser.Concrete;
using WebReaper.Domain.Parsing;

namespace WebReaper.UnitTests;

// The typed path added by ADR 0008 §2.12 step 1: the Schema fold's terminal
// projection is System.Text.Json.Nodes (JsonObject), beside the legacy JObject
// seam. These pin the typed path; the legacy JObject behaviour stays pinned by
// ParserTests / ListParsingTests / JsonParsingTests / XPathParsingTests /
// SchemaFoldTests (unchanged, served by the compat shim).
public class TypedFoldTests
{
    private static SchemaFold<AngleSharp.Dom.IParentNode> Html() =>
        new(new AngleSharpSchemaBackend(), NullLogger.Instance);
    private static SchemaFold<JsonNode> Json() =>
        new(new JsonSchemaBackend(), NullLogger.Instance);

    [Fact]
    public async Task Typed_path_folds_to_JsonObject_with_native_number()
    {
        JsonObject result = await Html().ExtractAsync(
            "<i>7</i>",
            new Schema { new SchemaElement("n", "i", DataType.Integer) });

        Assert.Equal(JsonValueKind.Number, result["n"]!.GetValueKind());
        Assert.Equal(7, result["n"]!.GetValue<int>());
    }

    [Fact]
    public async Task Untyped_leaf_divergence_is_preserved_in_JsonNode_terms()
    {
        // ADR 0002, now in System.Text.Json.Nodes: an untyped HTML leaf is a
        // string; an untyped JSON leaf keeps its native number. The JObject
        // terminal carried this via JToken.FromObject; the typed terminal must
        // carry it via the JToken->JsonNode bridge.
        var html = await Html().ExtractAsync(
            "<i>1</i><i>2</i>",
            new Schema { new SchemaElement("n", "i") { IsList = true } });
        var json = await Json().ExtractAsync(
            @"{ ""n"": [ 1, 2 ] }",
            new Schema { new SchemaElement("n", "$.n[*]") { IsList = true } });

        Assert.Equal(JsonValueKind.String, html["n"]!.AsArray()[0]!.GetValueKind());
        Assert.Equal(JsonValueKind.Number, json["n"]!.AsArray()[0]!.GetValueKind());
    }

    [Fact]
    public async Task Typed_coercion_is_backend_independent_on_typed_path()
    {
        var html = await Html().ExtractAsync(
            "<i>7</i><b>true</b>",
            new Schema
            {
                new SchemaElement("i", "i", DataType.Integer),
                new SchemaElement("b", "b", DataType.Boolean)
            });
        var json = await Json().ExtractAsync(
            @"{ ""i"": 7, ""b"": true }",
            new Schema
            {
                new SchemaElement("i", "$.i", DataType.Integer),
                new SchemaElement("b", "$.b", DataType.Boolean)
            });

        Assert.Equal(7, html["i"]!.GetValue<int>());
        Assert.True(html["b"]!.GetValue<bool>());
        Assert.True(JsonNode.DeepEquals(html, json));
    }

    [Fact]
    public async Task Missing_single_value_yields_empty_string_on_typed_path()
    {
        var html = await Html().ExtractAsync("<p>hi</p>",
            new Schema { new SchemaElement("x", ".nope") });
        var json = await Json().ExtractAsync(@"{ ""y"": 1 }",
            new Schema { new SchemaElement("x", "$.nope") });

        Assert.Equal("", html["x"]!.GetValue<string>());
        Assert.Equal("", json["x"]!.GetValue<string>());
    }

    [Fact]
    public async Task Object_list_folds_to_JsonArray_of_JsonObjects()
    {
        var result = await Html().ExtractAsync(
            "<div class='p'><span class='t'>a</span></div>" +
            "<div class='p'><span class='t'>b</span></div>",
            new Schema
            {
                new Schema("posts")
                {
                    IsList = true,
                    Selector = ".p",
                    Children = { new SchemaElement("t", ".t", DataType.String) }
                }
            });

        var posts = result["posts"]!.AsArray();
        Assert.Equal(2, posts.Count);
        Assert.Equal("a", posts[0]!["t"]!.GetValue<string>());
        Assert.Equal("b", posts[1]!["t"]!.GetValue<string>());
    }
}
