using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using WebReaper.Core.Parser.Concrete;
using WebReaper.Domain.Parsing;

namespace WebReaper.UnitTests;

// XPath backend (discussion #17), the third ISchemaBackend behind the one
// shared fold (ADR 0002 / 0007) — same assertions as JsonParsingTests, just
// XPath selectors over the AngleSharp DOM. Offline, no network.
public class XPathParsingTests
{
    private static XPathContentParser Parser() => new(NullLogger.Instance);

    [Fact]
    public async Task Parses_values_with_xpath_and_coerces_types()
    {
        const string html =
            "<html><body><div id='post'><h1 class='title'>Hello</h1>" +
            "<span class='views'>42</span></div></body></html>";

        var schema = new Schema
        {
            new SchemaElement("title", "//h1[@class='title']"),
            new SchemaElement("views", "//span[@class='views']", DataType.Integer)
        };

        var result = await Parser().ParseToJsonAsync(html, schema);

        Assert.Equal("Hello", result["title"]!.ToString());
        Assert.Equal(42, result["views"]!.GetValue<int>());
    }

    [Fact]
    public async Task Parses_a_list_of_objects_with_relative_xpath_children()
    {
        const string html =
            "<html><body><ul>" +
            "<li class='p'><span class='t'>A</span></li>" +
            "<li class='p'><span class='t'>B</span></li>" +
            "</ul></body></html>";

        var schema = new Schema
        {
            new Schema("posts")
            {
                Selector = "//li[@class='p']",
                IsList = true,
                Children = { new SchemaElement("t", ".//span[@class='t']") }
            }
        };

        var result = await Parser().ParseToJsonAsync(html, schema);

        var posts = Assert.IsType<JsonArray>(result["posts"]);
        Assert.Equal(2, posts.Count);
        Assert.Equal("A", posts[0]!["t"]!.ToString());
        Assert.Equal("B", posts[1]!["t"]!.ToString());
    }

    [Fact]
    public async Task Parses_a_scalar_list_keeping_coerced_types()
    {
        const string html = "<html><body><ul><li>1</li><li>2</li><li>3</li></ul></body></html>";

        var schema = new Schema
        {
            new SchemaElement("nums", "//li") { IsList = true, Type = DataType.Integer }
        };

        var result = await Parser().ParseToJsonAsync(html, schema);

        var nums = Assert.IsType<JsonArray>(result["nums"]);
        Assert.Equal(new[] { 1, 2, 3 }, nums.Select(n => n!.GetValue<int>()));
    }

    [Fact]
    public async Task Attribute_extraction_returns_the_requested_attribute_no_src_to_title_rewrite()
    {
        // The CSS backend rewrites a requested `src` to `title` (a quarantined
        // legacy quirk). The XPath backend deliberately does not (ADR 0007).
        const string html = "<html><body><a id='lnk' href='/x' src='/y' title='/z'>go</a></body></html>";

        var schema = new Schema
        {
            new SchemaElement("href", "//a[@id='lnk']", "href"),
            new SchemaElement("src", "//a[@id='lnk']", "src")
        };

        var result = await Parser().ParseToJsonAsync(html, schema);

        Assert.Equal("/x", result["href"]!.ToString());
        Assert.Equal("/y", result["src"]!.ToString());
    }
}
