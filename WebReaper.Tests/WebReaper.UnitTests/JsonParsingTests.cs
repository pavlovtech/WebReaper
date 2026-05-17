using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using WebReaper.Core.Parser.Concrete;
using WebReaper.Domain.Parsing;

namespace WebReaper.UnitTests
{
    public class JsonParsingTests
    {
        private static JsonContentParser Parser() => new(NullLogger.Instance);

        [Fact]
        public async Task ParsesNestedValuesWithJsonPath()
        {
            const string json = @"{ ""post"": { ""title"": ""Hello"", ""views"": 42 } }";

            var schema = new Schema
            {
                new SchemaElement("title", "post.title"),
                new SchemaElement("views", "post.views", DataType.Integer)
            };

            var result = await Parser().ParseToJsonAsync(json, schema);

            Assert.Equal("Hello", result["title"]!.ToString());
            Assert.Equal(42, result["views"]!.GetValue<int>());
        }

        [Fact]
        public async Task ParsesJsonArrayOfObjects()
        {
            const string json = @"{ ""posts"": [ { ""title"": ""A"" }, { ""title"": ""B"" } ] }";

            var schema = new Schema
            {
                new Schema("posts")
                {
                    Selector = "$.posts[*]",
                    IsList = true,
                    Children = { new SchemaElement("title", "title") }
                }
            };

            var result = await Parser().ParseToJsonAsync(json, schema);

            var posts = Assert.IsType<JsonArray>(result["posts"]);
            Assert.Equal(2, posts.Count);
            Assert.Equal("A", posts[0]!["title"]!.ToString());
            Assert.Equal("B", posts[1]!["title"]!.ToString());
        }

        // Characterization net (ADR-0008 JSONPath→STJ migration): the dialect
        // the codebase drives is "optional $ / $. root, dotted property path,
        // trailing [*] array wildcard". Pins two corners the rest of the JSON
        // suite leaves implicit — leading "$." is equivalent to a relative
        // path, and dotted paths nest arbitrarily deep — so the Newtonsoft→STJ
        // backend swap is behaviour-preserving, not just compiling.
        [Fact]
        public async Task DollarRootedAndRelativePathsAreEquivalentAndNestDeep()
        {
            const string json =
                @"{ ""a"": { ""b"": { ""c"": ""deep"" } }, ""x"": ""y"" }";

            var schema = new Schema
            {
                new SchemaElement("rooted", "$.a.b.c"),
                new SchemaElement("relative", "a.b.c"),
                new SchemaElement("shallowRooted", "$.x"),
                new SchemaElement("shallowRelative", "x")
            };

            var result = await Parser().ParseToJsonAsync(json, schema);

            Assert.Equal("deep", result["rooted"]!.ToString());
            Assert.Equal("deep", result["relative"]!.ToString());
            Assert.Equal("y", result["shallowRooted"]!.ToString());
            Assert.Equal("y", result["shallowRelative"]!.ToString());
        }

        [Fact]
        public async Task ParsesJsonArrayOfScalarsKeepingNativeTypes()
        {
            const string json = @"{ ""scores"": [ 1, 2, 3 ] }";

            var schema = new Schema
            {
                new SchemaElement("scores", "$.scores[*]") { IsList = true }
            };

            var result = await Parser().ParseToJsonAsync(json, schema);

            var scores = Assert.IsType<JsonArray>(result["scores"]);
            // ADR 0002 divergence: untyped JSON scalars stay native JSON
            // numbers (not strings). STJ collapses int/long to Number; the
            // contract is "a number, value N", not the CLR width.
            Assert.Equal(new[] { 1L, 2L, 3L }, scores.Select(s => s!.GetValue<long>()));
            Assert.Equal(JsonValueKind.Number, scores[0]!.GetValueKind());
        }
    }
}
