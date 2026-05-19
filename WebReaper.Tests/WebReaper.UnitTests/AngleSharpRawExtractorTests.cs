using AngleSharp;
using AngleSharp.Dom;
using WebReaper.Core.Parser.Concrete;
using WebReaper.Domain.Parsing;

namespace WebReaper.UnitTests;

// ADR-0027: the AngleSharp-DOM markup-leaf grammar lives in one home that
// both the CSS and XPath backends delegate to. Tests pin the three-arm
// dispatch (attribute / inner-HTML / text) and the missing-attribute
// empty-string contract at the helper's interface, so a future
// AngleSharp-DOM backend (Fizzler, …) inherits the grammar for free.
public class AngleSharpRawExtractorTests
{
    private static async Task<IElement> FirstElementAsync(string html, string css)
    {
        var context = BrowsingContext.New(Configuration.Default.WithDefaultLoader());
        var doc = await context.OpenAsync(resp =>
            resp.Header("Content-Type", "text/html; charset=utf-8").Content(html));
        var el = doc.QuerySelector(css);
        Assert.NotNull(el);
        return el!;
    }

    [Fact]
    public async Task Attribute_path_returns_the_requested_attribute()
    {
        var el = await FirstElementAsync(
            "<a id='lnk' href='/x' title='hello'>go</a>", "#lnk");

        var raw = AngleSharpRawExtractor.ExtractRaw(
            el, new SchemaElement("href", "#lnk", "href"));

        Assert.Equal("/x", raw);
    }

    [Fact]
    public async Task Missing_attribute_returns_empty_string_not_null()
    {
        var el = await FirstElementAsync(
            "<a id='lnk' href='/x'>go</a>", "#lnk");

        var raw = AngleSharpRawExtractor.ExtractRaw(
            el, new SchemaElement("missing", "#lnk", "data-nope"));

        Assert.Equal(string.Empty, raw);
    }

    [Fact]
    public async Task No_attr_default_returns_text_GetHtml_is_false_by_default()
    {
        // GetHtml is bool, defaults to false: with no Attr and no explicit
        // override, the helper returns text content, not InnerHtml.
        var el = await FirstElementAsync(
            "<div id='box'><span>x</span> <em>y</em></div>", "#box");

        var raw = AngleSharpRawExtractor.ExtractRaw(
            el, new SchemaElement("text", "#box"));

        Assert.Equal("x y", raw);
    }

    [Fact]
    public async Task No_attr_with_GetHtml_true_returns_inner_html()
    {
        var el = await FirstElementAsync(
            "<div id='box'><span>x</span> <em>y</em></div>", "#box");

        var raw = AngleSharpRawExtractor.ExtractRaw(
            el, new SchemaElement("html", "#box", true)); // getHtml: true

        Assert.Equal("<span>x</span> <em>y</em>", raw);
    }

    [Fact]
    public async Task Helper_does_not_apply_the_CSS_src_to_title_quirk()
    {
        // ADR-0007 says the src→title rewrite is a CSS-backend-local
        // quirk; the helper itself is quirk-free. A consumer that asks
        // for src gets src.
        var el = await FirstElementAsync(
            "<img id='i' src='/y' title='/z'>", "#i");

        var raw = AngleSharpRawExtractor.ExtractRaw(
            el, new SchemaElement("u", "#i", "src"));

        Assert.Equal("/y", raw);
    }

    [Fact]
    public async Task Helper_does_not_mutate_the_SchemaElement_Attr()
    {
        // The src→title in-place mutation is the CSS backend's own
        // (applied before delegation, pinned by SchemaFoldTests). The
        // helper must leave Attr alone — so a third AngleSharp backend
        // can delegate without inheriting the CSS quirk.
        var el = await FirstElementAsync(
            "<img id='i' src='/y' title='/z'>", "#i");
        var schemaElement = new SchemaElement("u", "#i", "src");

        AngleSharpRawExtractor.ExtractRaw(el, schemaElement);

        Assert.Equal("src", schemaElement.Attr);
    }
}
