using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
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

            var result = await Parser().ParseToJsonAsync(ListingsHtml, schema);

            var listings = Assert.IsType<JsonArray>(result["listings"]);
            Assert.Equal(3, listings.Count);
            Assert.Equal("Alpha", listings[0]!["name"]!.ToString());
            Assert.Equal(10, listings[0]!["price"]!.GetValue<int>());
            Assert.Equal("Gamma", listings[2]!["name"]!.ToString());
            Assert.Equal(30, listings[2]!["price"]!.GetValue<int>());
        }

        [Fact]
        public async Task ParsesListOfScalars()
        {
            var schema = new Schema
            {
                new SchemaElement("names", ".name") { IsList = true }
            };

            var result = await Parser().ParseToJsonAsync(ListingsHtml, schema);

            var names = Assert.IsType<JsonArray>(result["names"]);
            Assert.Equal(new[] { "Alpha", "Beta", "Gamma" }, names.Select(n => n!.ToString()));
        }

        [Fact]
        public async Task NonListSchemaStillReturnsFirstMatchOnly()
        {
            // Backward compatibility: IsList defaults to false.
            var schema = new Schema
            {
                new SchemaElement("name", ".name")
            };

            var result = await Parser().ParseToJsonAsync(ListingsHtml, schema);

            Assert.Equal("Alpha", result["name"]!.ToString());
        }

        [Fact]
        public void ConstructingALeafListWithoutASelectorThrowsAtTheAddSite()
        {
            // ADR-0028: the empty-selector leaf-list used to be swallowed by
            // the fold's per-leaf catch — field left silently unset. Now the
            // Add validation fast-fails at construction, the exact line the
            // user wrote the bad element.
            var ex = Assert.Throws<ArgumentException>(() => new Schema
            {
                new SchemaElement("names", selector: "") { IsList = true }
            });

            Assert.Contains("Leaf 'names' must have a non-empty Selector", ex.Message);
        }

        [Fact]
        public void ConstructingAnObjectListWithoutASelectorAlsoThrowsAtTheAddSite()
        {
            // ADR-0028: the same fast-fail for object-lists. Previously this
            // path was *more* dangerous — the fold's container branch was
            // unguarded by the per-leaf catch, so a missing Selector aborted
            // the whole parse mid-flight. Construction-time validation makes
            // the two arms uniform.
            var ex = Assert.Throws<ArgumentException>(() => new Schema
            {
                new Schema("listings")
                {
                    IsList = true,
                    Children =
                    {
                        new SchemaElement("name", ".name")
                    }
                    // Selector deliberately omitted.
                }
            });

            Assert.Contains("List container 'listings'", ex.Message);
        }
    }
}
