using System.Text.Json.Nodes;
using WebReaper.Core.Parser.Concrete;
using WebReaper.Domain.Parsing;

namespace WebReaper.UnitTests;

// ADR-0040: the no-schema Markdown adapter of the IContentExtractor seam.
// Tests pin the funnel's wedge: a Crawl(...).AsMarkdown() crawl returns a
// {title, markdown} JsonObject containing LLM-ready Markdown of the main
// content area, without an LLM dependency. The strategy-local schema
// requirement is also pinned — a passed Schema must be silently ignored.
public class MarkdownContentExtractorTests
{
    private static MarkdownContentExtractor Make() => new();

    private static async Task<JsonObject> Run(string html, Schema? schema = null)
        => await Make().ExtractAsync(html, schema);

    private static string Markdown(JsonObject result) =>
        result["markdown"]!.GetValue<string>();

    private static string Title(JsonObject result) =>
        result["title"]!.GetValue<string>();

    [Fact]
    public async Task A_simple_article_renders_to_markdown_and_a_title()
    {
        // The funnel's wedge: smallest possible call returns LLM-ready
        // text. Asserts the shape (title + markdown), the content
        // (heading and paragraph survive), and the title sourcing (h1
        // inside main wins over the <title> tag).
        var result = await Run(
            "<html><head><title>From the head</title></head><body>" +
            "<article><h1>Hello</h1><p>This is a paragraph.</p></article>" +
            "</body></html>");

        Assert.Equal("Hello", Title(result));
        Assert.Contains("# Hello", Markdown(result));
        Assert.Contains("This is a paragraph.", Markdown(result));
    }

    [Fact]
    public async Task Title_falls_back_to_head_when_main_has_no_h1()
    {
        // ADR-0040: h1 inside main wins; else <head><title>. A page with
        // no in-content h1 uses the head's title — important because
        // many editorial pages put the headline outside <h1>.
        var result = await Run(
            "<html><head><title>Falls back</title></head><body>" +
            "<main><p>No heading here.</p></main></body></html>");

        Assert.Equal("Falls back", Title(result));
    }

    [Fact]
    public async Task Nav_and_footer_are_stripped_from_main_content()
    {
        // The Readability heuristic: nav/aside/footer/header/script
        // never reach the renderer. A site's chrome must not leak into
        // the Markdown the LLM sees.
        var result = await Run(
            "<html><body>" +
            "<header><h1>Navigation Title</h1></header>" +
            "<nav><a href='/home'>Home</a></nav>" +
            "<aside><p>Related links</p></aside>" +
            "<article><h1>Real Title</h1><p>Real content.</p></article>" +
            "<footer><p>Copyright</p></footer>" +
            "</body></html>");

        var md = Markdown(result);
        Assert.Contains("Real content.", md);
        Assert.Equal("Real Title", Title(result));
        Assert.DoesNotContain("Navigation Title", md);
        Assert.DoesNotContain("Related links", md);
        Assert.DoesNotContain("Copyright", md);
        Assert.DoesNotContain("Home", md);
    }

    [Fact]
    public async Task Script_style_and_hidden_elements_are_stripped()
    {
        // Script/style/noscript/hidden content is mandatory stripping
        // for correctness — these never represent reading content.
        var result = await Run(
            "<html><body><article>" +
            "<script>var bad = 1;</script>" +
            "<style>.x { color: red; }</style>" +
            "<p hidden>Hidden text</p>" +
            "<div aria-hidden='true'>Aria-hidden text</div>" +
            "<p>Visible text</p>" +
            "</article></body></html>");

        var md = Markdown(result);
        Assert.Contains("Visible text", md);
        Assert.DoesNotContain("var bad", md);
        Assert.DoesNotContain("color: red", md);
        Assert.DoesNotContain("Hidden text", md);
        Assert.DoesNotContain("Aria-hidden", md);
    }

    [Fact]
    public async Task Heading_levels_round_trip_h1_through_h6()
    {
        var result = await Run(
            "<article>" +
            "<h1>One</h1><h2>Two</h2><h3>Three</h3>" +
            "<h4>Four</h4><h5>Five</h5><h6>Six</h6>" +
            "</article>");

        var md = Markdown(result);
        Assert.Contains("# One", md);
        Assert.Contains("## Two", md);
        Assert.Contains("### Three", md);
        Assert.Contains("#### Four", md);
        Assert.Contains("##### Five", md);
        Assert.Contains("###### Six", md);
    }

    [Fact]
    public async Task Inline_emphasis_renders_as_markdown_emphasis()
    {
        var result = await Run(
            "<article><p>This is <strong>bold</strong> and <em>italic</em> " +
            "and <code>inline</code> and <del>struck</del>.</p></article>");

        var md = Markdown(result);
        Assert.Contains("**bold**", md);
        Assert.Contains("*italic*", md);
        Assert.Contains("`inline`", md);
        Assert.Contains("~~struck~~", md);
    }

    [Fact]
    public async Task Links_and_images_use_markdown_syntax()
    {
        var result = await Run(
            "<article><p>See <a href='/x'>here</a>.</p>" +
            "<p><img src='/img.png' alt='An image'/></p></article>");

        var md = Markdown(result);
        Assert.Contains("[here](/x)", md);
        Assert.Contains("![An image](/img.png)", md);
    }

    [Fact]
    public async Task Anchors_without_href_keep_their_text()
    {
        // A bare <a> shouldn't render an empty link; we keep just the
        // text. Avoids "()" or "[]" garbage in the Markdown output.
        var result = await Run(
            "<article><p>Plain <a>anchor</a> text.</p></article>");

        var md = Markdown(result);
        Assert.Contains("Plain anchor text.", md);
        Assert.DoesNotContain("[anchor]()", md);
    }

    [Fact]
    public async Task Unordered_list_renders_with_dash_markers()
    {
        var result = await Run(
            "<article><ul><li>First</li><li>Second</li><li>Third</li></ul></article>");

        var md = Markdown(result);
        Assert.Contains("- First", md);
        Assert.Contains("- Second", md);
        Assert.Contains("- Third", md);
    }

    [Fact]
    public async Task Ordered_list_renders_with_numbered_markers()
    {
        var result = await Run(
            "<article><ol><li>First</li><li>Second</li><li>Third</li></ol></article>");

        var md = Markdown(result);
        Assert.Contains("1. First", md);
        Assert.Contains("2. Second", md);
        Assert.Contains("3. Third", md);
    }

    [Fact]
    public async Task Nested_list_indents_by_two_spaces_per_level()
    {
        var result = await Run(
            "<article><ul>" +
            "<li>Outer<ul><li>Inner</li></ul></li>" +
            "</ul></article>");

        var md = Markdown(result);
        Assert.Contains("- Outer", md);
        // Nested item indented by two spaces.
        Assert.Contains("  - Inner", md);
    }

    [Fact]
    public async Task Blockquote_renders_with_gt_prefix()
    {
        var result = await Run(
            "<article><blockquote><p>A quote.</p></blockquote></article>");

        var md = Markdown(result);
        Assert.Contains("> A quote.", md);
    }

    [Fact]
    public async Task Code_fence_uses_language_class_when_present()
    {
        var result = await Run(
            "<article><pre><code class='language-csharp'>" +
            "var x = 1;" +
            "</code></pre></article>");

        var md = Markdown(result);
        Assert.Contains("```csharp", md);
        Assert.Contains("var x = 1;", md);
        Assert.Contains("```", md);
    }

    [Fact]
    public async Task Hr_renders_as_three_dashes_block()
    {
        var result = await Run(
            "<article><p>Above.</p><hr/><p>Below.</p></article>");

        var md = Markdown(result);
        Assert.Contains("---", md);
    }

    [Fact]
    public async Task Table_renders_in_gfm_grammar()
    {
        var result = await Run(
            "<article><table>" +
            "<thead><tr><th>A</th><th>B</th></tr></thead>" +
            "<tbody><tr><td>1</td><td>2</td></tr></tbody>" +
            "</table></article>");

        var md = Markdown(result);
        Assert.Contains("| A | B |", md);
        Assert.Contains("| --- | --- |", md);
        Assert.Contains("| 1 | 2 |", md);
    }

    [Fact]
    public async Task Table_treats_first_row_as_header_when_no_thead()
    {
        // Many scraped tables omit <thead>; common shape — first <tr>
        // serves as the header row.
        var result = await Run(
            "<article><table>" +
            "<tr><td>Name</td><td>Value</td></tr>" +
            "<tr><td>x</td><td>1</td></tr>" +
            "</table></article>");

        var md = Markdown(result);
        Assert.Contains("| Name | Value |", md);
        Assert.Contains("| --- | --- |", md);
        Assert.Contains("| x | 1 |", md);
    }

    [Fact]
    public async Task Main_content_falls_back_to_body_when_no_article_or_main()
    {
        // Pages with neither <article> nor <main> still render — the
        // heuristic falls back to <body>. Stripped tags still apply.
        var result = await Run(
            "<html><body>" +
            "<nav>Nav</nav>" +
            "<h1>Page title</h1>" +
            "<p>Body content.</p>" +
            "</body></html>");

        var md = Markdown(result);
        Assert.Contains("# Page title", md);
        Assert.Contains("Body content.", md);
        Assert.DoesNotContain("Nav", md);
    }

    [Fact]
    public async Task Role_main_attribute_is_recognised_as_main_content()
    {
        // ARIA: <div role="main"> is a common React/SPA pattern where
        // semantic <main> isn't used. Heuristic must pick it up.
        var result = await Run(
            "<html><body>" +
            "<nav>Skip</nav>" +
            "<div role='main'><h1>Found</h1><p>Captured.</p></div>" +
            "</body></html>");

        var md = Markdown(result);
        Assert.Contains("# Found", md);
        Assert.Contains("Captured.", md);
        Assert.DoesNotContain("Skip", md);
    }

    [Fact]
    public async Task Output_url_field_is_not_set_by_the_extractor()
    {
        // ADR-0031: ParsedData construction folds in "url". The
        // extractor must leave it alone; only "title" and "markdown".
        var result = await Run(
            "<article><h1>x</h1><p>y</p></article>");

        Assert.False(result.ContainsKey("url"));
        Assert.True(result.ContainsKey("title"));
        Assert.True(result.ContainsKey("markdown"));
    }

    [Fact]
    public async Task Passing_a_schema_is_silently_ignored()
    {
        // ADR-0040 §2: the Markdown strategy is permissive about its
        // unused Schema. A caller composing a Schema and Markdown
        // (e.g. via a future router) must not get a throw here.
        var schema = new Schema
        {
            new SchemaElement("ignored", ".title", DataType.String)
        };

        var result = await Run(
            "<article><h1>Title</h1><p>Body.</p></article>", schema);

        Assert.Equal("Title", Title(result));
        Assert.Contains("Body.", Markdown(result));
        Assert.False(result.ContainsKey("ignored"));
    }

    [Fact]
    public async Task Whitespace_is_normalised_to_at_most_one_blank_line()
    {
        // Several paragraphs collapse to clean blocks separated by
        // exactly one blank line — no run of 3+ newlines anywhere.
        var result = await Run(
            "<article><p>A.</p><p>B.</p><p>C.</p></article>");

        var md = Markdown(result);
        Assert.DoesNotContain("\n\n\n", md);
        Assert.DoesNotMatch(@"^\s*\n", md);
        Assert.DoesNotMatch(@"\n\s*$", md);
    }

    [Fact]
    public async Task Article_inside_main_prefers_article_over_main()
    {
        // The heuristic order: <article> wins over <main>. Common shape
        // for editorial pages — <main> wrapping <article>.
        var result = await Run(
            "<html><body><main>" +
            "<article><h1>Article wins</h1><p>From article.</p></article>" +
            "<p>From main outside article.</p>" +
            "</main></body></html>");

        var md = Markdown(result);
        Assert.Contains("From article.", md);
        Assert.DoesNotContain("outside article", md);
    }
}
