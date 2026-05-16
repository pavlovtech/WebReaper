using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using WebReaper.Core.Parser.Abstract;
using WebReaper.Core.Parser.Concrete;
using WebReaper.Domain.Parsing;

namespace WebReaper.UnitTests;

// The Schema fold, exercised directly through the seam. The legacy
// per-backend behaviour is already pinned by ParserTests / ListParsingTests
// / JsonParsingTests; these tests pin what the *deepening* added: one fold
// serving every backend, the divergence/quirks being deliberate and
// quarantined, and a brand-new backend being a tiny adapter — not a copy of
// the walk.
public class SchemaFoldTests
{
    private static AngleSharpContentParser Html() => new(NullLogger.Instance);
    private static JsonContentParser Json() => new(NullLogger.Instance);

    [Fact]
    public async Task Same_schema_shape_folds_to_the_same_structure_on_html_and_json()
    {
        // Selectors are inherently backend-specific (CSS vs JSONPath); the
        // grammar is what's shared. With typed leaves, coercion normalises
        // both sides, so the assembled JObjects must be byte-identical.
        const string html =
            "<html><body><div class='post'>" +
            "<h1 class='title'>Hello</h1><span class='views'>42</span>" +
            "<ul><li class='tag'>a</li><li class='tag'>b</li></ul>" +
            "</div></body></html>";
        const string json =
            @"{ ""post"": { ""title"": ""Hello"", ""views"": 42, ""tags"": [ ""a"", ""b"" ] } }";

        var htmlResult = await Html().ParseAsync(html, new Schema
        {
            new Schema("post")
            {
                Children =
                {
                    new SchemaElement("title", ".title", DataType.String),
                    new SchemaElement("views", ".views", DataType.Integer),
                    new SchemaElement("tags", ".tag", DataType.String) { IsList = true }
                }
            }
        });

        var jsonResult = await Json().ParseAsync(json, new Schema
        {
            new Schema("post")
            {
                Children =
                {
                    new SchemaElement("title", "post.title", DataType.String),
                    new SchemaElement("views", "post.views", DataType.Integer),
                    new SchemaElement("tags", "post.tags[*]", DataType.String) { IsList = true }
                }
            }
        });

        Assert.True(JToken.DeepEquals(htmlResult, jsonResult),
            $"html={htmlResult.ToString(Newtonsoft.Json.Formatting.None)} " +
            $"json={jsonResult.ToString(Newtonsoft.Json.Formatting.None)}");
    }

    [Fact]
    public async Task Untyped_leaf_divergence_is_deliberate_html_string_vs_json_native()
    {
        // DELIBERATE, see docs/adr/0002: an untyped leaf is the backend's
        // raw value verbatim — HTML text is a string, a JSON number stays a
        // number. Do not "unify" this; that would silently regress every
        // JSON-endpoint user. This test fails loudly if someone tries.
        var htmlList = await Html().ParseAsync(
            "<i>1</i><i>2</i>",
            new Schema { new SchemaElement("n", "i") { IsList = true } });
        var jsonList = await Json().ParseAsync(
            @"{ ""n"": [ 1, 2 ] }",
            new Schema { new SchemaElement("n", "$.n[*]") { IsList = true } });

        Assert.Equal(JTokenType.String, ((JArray)htmlList["n"]!)[0].Type);
        Assert.Equal(JTokenType.Integer, ((JArray)jsonList["n"]!)[0].Type);
    }

    [Fact]
    public async Task Src_to_title_rewrite_is_quarantined_in_the_html_backend()
    {
        var element = new SchemaElement("u", ".img", "src");

        var result = await Html().ParseAsync(
            "<img class='img' src='SRC' title='TITLE'>",
            new Schema { element });

        Assert.Equal("TITLE", result["u"]!.ToString()); // src was rewritten to title
        Assert.Equal("title", element.Attr);            // the historical mutation, preserved
    }

    [Fact]
    public async Task Json_backend_has_no_attr_notion_so_src_is_ignored_and_unmutated()
    {
        var element = new SchemaElement("u", "u", "src");

        var result = await Json().ParseAsync(@"{ ""u"": ""V"" }", new Schema { element });

        Assert.Equal("V", result["u"]!.ToString());
        Assert.Equal("src", element.Attr); // quarantine boundary: no mutation on this side
    }

    [Fact]
    public async Task Typed_coercion_is_backend_independent()
    {
        var html = await Html().ParseAsync(
            "<i>7</i><b>true</b>",
            new Schema
            {
                new SchemaElement("i", "i", DataType.Integer),
                new SchemaElement("b", "b", DataType.Boolean)
            });
        var json = await Json().ParseAsync(
            @"{ ""i"": 7, ""b"": true }",
            new Schema
            {
                new SchemaElement("i", "$.i", DataType.Integer),
                new SchemaElement("b", "$.b", DataType.Boolean)
            });

        Assert.True(JToken.DeepEquals(html, json));
        Assert.Equal(7, html["i"]!.Value<int>());
        Assert.True(html["b"]!.Value<bool>());
    }

    [Fact]
    public async Task Missing_single_value_yields_empty_string_on_every_backend()
    {
        var schema = () => new Schema { new SchemaElement("x", "nope") };

        var html = await Html().ParseAsync("<p>hi</p>",
            new Schema { new SchemaElement("x", ".nope") });
        var json = await Json().ParseAsync(@"{ ""y"": 1 }",
            new Schema { new SchemaElement("x", "$.nope") });
        var custom = await new SchemaContentParser<KeyValueNode>(new KeyValueBackend(), NullLogger.Instance)
            .ParseAsync("y=1", schema());

        Assert.Equal(string.Empty, html["x"]!.ToString());
        Assert.Equal(string.Empty, json["x"]!.ToString());
        Assert.Equal(string.Empty, custom["x"]!.ToString());
    }

    [Fact]
    public async Task A_new_backend_is_a_tiny_adapter_reusing_the_fold()
    {
        // The deepening's deliverable: a document shape that is neither HTML
        // nor JSON runs through the SAME fold (coercion, JObject assembly,
        // missing-node policy) with a ~15-line ISchemaBackend and zero
        // copied walk.
        var parser = new SchemaContentParser<KeyValueNode>(new KeyValueBackend(), NullLogger.Instance);

        var result = await parser.ParseAsync(
            "title=Hello\nviews=42\ntag=a\ntag=b",
            new Schema
            {
                new SchemaElement("title", "title", DataType.String),
                new SchemaElement("views", "views", DataType.Integer),
                new SchemaElement("tags", "tag", DataType.String) { IsList = true }
            });

        Assert.Equal("Hello", result["title"]!.ToString());
        Assert.Equal(42, result["views"]!.Value<int>());
        Assert.Equal(new[] { "a", "b" }, ((JArray)result["tags"]!).Select(t => t.ToString()));
    }

    // --- a deliberately foreign document model: line-based "key=value" ---

    private sealed class KeyValueNode
    {
        public ILookup<string, string>? Root { get; init; }
        public string? Value { get; init; }
    }

    private sealed class KeyValueBackend : ISchemaBackend<KeyValueNode>
    {
        public Task<KeyValueNode> RootAsync(string content)
        {
            var root = content
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(line => line.Split('=', 2))
                .ToLookup(p => p[0], p => p[1]);

            return Task.FromResult(new KeyValueNode { Root = root });
        }

        public IEnumerable<KeyValueNode> SelectMany(KeyValueNode scope, string selector)
            => scope.Root![selector].Select(v => new KeyValueNode { Value = v });

        public KeyValueNode? SelectOne(KeyValueNode scope, string selector)
            => scope.Root![selector].FirstOrDefault() is { } v ? new KeyValueNode { Value = v } : null;

        public object? ExtractRaw(KeyValueNode node, SchemaElement element) => node.Value;
    }
}
