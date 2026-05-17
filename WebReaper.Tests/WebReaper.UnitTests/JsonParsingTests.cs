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
