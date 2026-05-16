using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using WebReaper.Core.Parser.Concrete;
using WebReaper.Domain.Parsing;

namespace WebReaper.UnitTests
{
    public class ListParsingTests
    {
        private static AngleSharpContentParser Parser() => new(NullLogger.Instance);

        private const string ListingsHtml = @"
            <html><body>
              <div class='card'>
                <h2 class='name'>Alpha</h2><span class='price'>10</span>
              </div>
              <div class='card'>
                <h2 class='name'>Beta</h2><span class='price'>20</span>
              </div>
              <div class='card'>
                <h2 class='name'>Gamma</h2><span class='price'>30</span>
              </div>
            </body></html>";

        [Fact]
        public async Task ParsesListOfObjectsWithElementScopedChildren()
        {
            var schema = new Schema
            {
                new Schema("listings")
                {
                    Selector = ".card",
                    IsList = true,
                    Children =
                    {
                        new SchemaElement("name", ".name"),
                        new SchemaElement("price", ".price", DataType.Integer)
                    }
                }
            };

            var result = await Parser().ParseAsync(ListingsHtml, schema);

            var listings = Assert.IsType<JArray>(result["listings"]);
            Assert.Equal(3, listings.Count);
            Assert.Equal("Alpha", listings[0]!["name"]!.ToString());
            Assert.Equal(10, listings[0]!["price"]!.Value<int>());
            Assert.Equal("Gamma", listings[2]!["name"]!.ToString());
            Assert.Equal(30, listings[2]!["price"]!.Value<int>());
        }

        [Fact]
        public async Task ParsesListOfScalars()
        {
            var schema = new Schema
            {
                new SchemaElement("names", ".name") { IsList = true }
            };

            var result = await Parser().ParseAsync(ListingsHtml, schema);

            var names = Assert.IsType<JArray>(result["names"]);
            Assert.Equal(new[] { "Alpha", "Beta", "Gamma" }, names.Select(n => n.ToString()));
        }

        [Fact]
        public async Task NonListSchemaStillReturnsFirstMatchOnly()
        {
            // Backward compatibility: IsList defaults to false.
            var schema = new Schema
            {
                new SchemaElement("name", ".name")
            };

            var result = await Parser().ParseAsync(ListingsHtml, schema);

            Assert.Equal("Alpha", result["name"]!.ToString());
        }

        [Fact]
        public async Task ListSchemaWithoutSelectorThrows()
        {
            var schema = new Schema
            {
                new SchemaElement("names", selector: "") { IsList = true }
            };

            // Thrown inside FillOutput's try for leaf elements -> logged,
            // field left unset rather than crashing the whole parse.
            var result = await Parser().ParseAsync(ListingsHtml, schema);
            Assert.Null(result["names"]);
        }
    }
}
