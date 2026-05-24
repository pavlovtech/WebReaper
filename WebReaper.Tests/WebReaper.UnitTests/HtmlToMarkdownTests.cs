using WebReaper.Core.Markdown;

namespace WebReaper.UnitTests;

// ADR-0063: the public HtmlToMarkdown primitive. These tests pin the
// pure-function shape of the conversion — the heuristic, the strip list,
// the GFM rendering, and the title resolution. They are the canonical
// rendering tests; the MarkdownContentExtractor adapter is a thin shell
// over this primitive and its own tests verify the JsonObject projection
// only.
public class HtmlToMarkdownTests
{
    // ---------- The heuristic: article > main > [role=main] > body ----------

    [Fact]
    public void Article_wins_over_body()
    {
        // The first arm of the heuristic — when an <article> exists,
        // body content outside it is ignored.
        var content = HtmlToMarkdown.ExtractMainContent(
            "<html><body>" +
            "<p>Outside the article.</p>" +
            "<article><h1>Inside</h1><p>From article.</p></article>" +
            "</body></html>");

        Assert.Contains("From article.", content.Markdown);
        Assert.DoesNotContain("Outside the article.", content.Markdown);
    }

    [Fact]
    public void Main_wins_when_no_article_present()
    {
        // The second arm — fall back to <main> when no <article>.
        var content = HtmlToMarkdown.ExtractMainContent(
            "<html><body>" +
            "<p>Outside main.</p>" +
            "<main><h1>From main</h1><p>Main content.</p></main>" +
            "</body></html>");

        Assert.Contains("Main content.", content.Markdown);
        Assert.DoesNotContain("Outside main.", content.Markdown);
    }

    [Fact]
    public void Role_main_wins_when_no_article_or_main_tag()
    {
        // The third arm — ARIA role="main" is a common SPA / React
        // pattern where the semantic <main> tag isn't used.
        var content = HtmlToMarkdown.ExtractMainContent(
            "<html><body>" +
            "<p>Outside role-main.</p>" +
            "<div role='main'><h1>Role main</h1><p>Captured.</p></div>" +
            "</body></html>");

        Assert.Contains("Captured.", content.Markdown);
        Assert.DoesNotContain("Outside role-main.", content.Markdown);
    }

    [Fact]
    public void Body_is_the_final_fallback()
    {
        // Last arm — no <article>, no <main>, no [role=main]. <body>
        // wins; stripped chrome still applies.
        var content = HtmlToMarkdown.ExtractMainContent(
            "<html><body>" +
            "<nav>Nav</nav>" +
            "<h1>Page title</h1>" +
            "<p>Body content.</p>" +
            "</body></html>");

        Assert.Contains("# Page title", content.Markdown);
        Assert.Contains("Body content.", content.Markdown);
        Assert.DoesNotContain("Nav", content.Markdown);
    }

    [Fact]
    public void Article_inside_main_prefers_article()
    {
        // Common shape — <main> wraps <article>. <article> wins; the
        // surrounding <main> content outside the article is dropped.
        var content = HtmlToMarkdown.ExtractMainContent(
            "<html><body><main>" +
            "<article><h1>Article wins</h1><p>From article.</p></article>" +
            "<p>From main outside article.</p>" +
            "</main></body></html>");

        Assert.Contains("From article.", content.Markdown);
        Assert.DoesNotContain("outside article", content.Markdown);
    }

    // ---------- The strip list ----------

    [Fact]
    public void Nav_is_stripped()
    {
        var content = HtmlToMarkdown.ExtractMainContent(
            "<article><nav>Skip to content</nav><h1>X</h1><p>Body.</p></article>");

        Assert.Contains("Body.", content.Markdown);
        Assert.DoesNotContain("Skip to content", content.Markdown);
    }

    [Fact]
    public void Footer_is_stripped()
    {
        var content = HtmlToMarkdown.ExtractMainContent(
            "<article><h1>X</h1><p>Body.</p><footer>Copyright.</footer></article>");

        Assert.Contains("Body.", content.Markdown);
        Assert.DoesNotContain("Copyright.", content.Markdown);
    }

    [Fact]
    public void Aside_is_stripped()
    {
        var content = HtmlToMarkdown.ExtractMainContent(
            "<article><h1>X</h1><aside><p>Related.</p></aside><p>Body.</p></article>");

        Assert.Contains("Body.", content.Markdown);
        Assert.DoesNotContain("Related.", content.Markdown);
    }

    [Fact]
    public void Script_is_stripped()
    {
        var content = HtmlToMarkdown.ExtractMainContent(
            "<article><h1>X</h1><script>var leak = 1;</script><p>Body.</p></article>");

        Assert.Contains("Body.", content.Markdown);
        Assert.DoesNotContain("var leak", content.Markdown);
    }

    [Fact]
    public void Style_is_stripped()
    {
        var content = HtmlToMarkdown.ExtractMainContent(
            "<article><h1>X</h1><style>.x { color: red; }</style><p>Body.</p></article>");

        Assert.Contains("Body.", content.Markdown);
        Assert.DoesNotContain("color: red", content.Markdown);
    }

    [Fact]
    public void Noscript_is_stripped()
    {
        var content = HtmlToMarkdown.ExtractMainContent(
            "<article><h1>X</h1><noscript>Enable JS.</noscript><p>Body.</p></article>");

        Assert.Contains("Body.", content.Markdown);
        Assert.DoesNotContain("Enable JS.", content.Markdown);
    }

    [Fact]
    public void Aria_hidden_and_hidden_attributes_are_stripped()
    {
        var content = HtmlToMarkdown.ExtractMainContent(
            "<article>" +
            "<p hidden>Hidden paragraph.</p>" +
            "<div aria-hidden='true'>Aria-hidden.</div>" +
            "<p>Visible.</p>" +
            "</article>");

        Assert.Contains("Visible.", content.Markdown);
        Assert.DoesNotContain("Hidden paragraph.", content.Markdown);
        Assert.DoesNotContain("Aria-hidden.", content.Markdown);
    }

    // ---------- GFM rendering — basics ----------

    [Fact]
    public void All_heading_levels_render_with_correct_hash_count()
    {
        var content = HtmlToMarkdown.ExtractMainContent(
            "<article>" +
            "<h1>One</h1><h2>Two</h2><h3>Three</h3>" +
            "<h4>Four</h4><h5>Five</h5><h6>Six</h6>" +
            "</article>");

        Assert.Contains("# One", content.Markdown);
        Assert.Contains("## Two", content.Markdown);
        Assert.Contains("### Three", content.Markdown);
        Assert.Contains("#### Four", content.Markdown);
        Assert.Contains("##### Five", content.Markdown);
        Assert.Contains("###### Six", content.Markdown);
    }

    [Fact]
    public void Paragraphs_separate_with_one_blank_line()
    {
        var content = HtmlToMarkdown.ExtractMainContent(
            "<article><p>First.</p><p>Second.</p></article>");

        // Two paragraphs separated by exactly one blank line.
        Assert.Matches(@"First\.\s*\n\n+Second\.", content.Markdown);
        // No run of three or more newlines.
        Assert.DoesNotContain("\n\n\n", content.Markdown);
    }

    [Fact]
    public void Unordered_list_uses_dash_markers()
    {
        var content = HtmlToMarkdown.ExtractMainContent(
            "<article><ul><li>A</li><li>B</li></ul></article>");

        Assert.Contains("- A", content.Markdown);
        Assert.Contains("- B", content.Markdown);
    }

    [Fact]
    public void Ordered_list_uses_numbered_markers()
    {
        var content = HtmlToMarkdown.ExtractMainContent(
            "<article><ol><li>A</li><li>B</li><li>C</li></ol></article>");

        Assert.Contains("1. A", content.Markdown);
        Assert.Contains("2. B", content.Markdown);
        Assert.Contains("3. C", content.Markdown);
    }

    [Fact]
    public void Link_renders_with_markdown_syntax()
    {
        var content = HtmlToMarkdown.ExtractMainContent(
            "<article><p>See <a href='/x'>here</a>.</p></article>");

        Assert.Contains("[here](/x)", content.Markdown);
    }

    [Fact]
    public void Image_renders_with_markdown_syntax_and_alt_text()
    {
        var content = HtmlToMarkdown.ExtractMainContent(
            "<article><p><img src='/img.png' alt='An image'/></p></article>");

        Assert.Contains("![An image](/img.png)", content.Markdown);
    }

    [Fact]
    public void Code_block_renders_as_fenced_block()
    {
        var content = HtmlToMarkdown.ExtractMainContent(
            "<article><pre><code class='language-csharp'>var x = 1;</code></pre></article>");

        Assert.Contains("```csharp", content.Markdown);
        Assert.Contains("var x = 1;", content.Markdown);
        // The closing fence is also present.
        Assert.Equal(2, content.Markdown.Split("```").Length - 1);
    }

    [Fact]
    public void Blockquote_renders_with_gt_prefix()
    {
        var content = HtmlToMarkdown.ExtractMainContent(
            "<article><blockquote><p>A quote.</p></blockquote></article>");

        Assert.Contains("> A quote.", content.Markdown);
    }

    // ---------- Title resolution ----------

    [Fact]
    public void Title_uses_h1_inside_main_when_present()
    {
        // Per the heuristic — the first surviving <h1> inside main
        // content wins over <head><title>.
        var content = HtmlToMarkdown.ExtractMainContent(
            "<html><head><title>From the head</title></head><body>" +
            "<article><h1>From the body</h1><p>x</p></article>" +
            "</body></html>");

        Assert.Equal("From the body", content.Title);
    }

    [Fact]
    public void Title_falls_back_to_head_title_when_no_h1_in_main()
    {
        var content = HtmlToMarkdown.ExtractMainContent(
            "<html><head><title>From the head</title></head><body>" +
            "<main><p>No heading here.</p></main>" +
            "</body></html>");

        Assert.Equal("From the head", content.Title);
    }

    [Fact]
    public void Title_ignores_h1_in_stripped_chrome()
    {
        // A <header><h1> outside <article> is part of the site chrome;
        // it gets stripped, so the title must NOT pick it up.
        var content = HtmlToMarkdown.ExtractMainContent(
            "<html><head><title>From the head</title></head><body>" +
            "<header><h1>Nav title</h1></header>" +
            "<article><p>No h1 here.</p></article>" +
            "</body></html>");

        // Falls back to head/title — Nav title was stripped.
        Assert.Equal("From the head", content.Title);
    }

    [Fact]
    public void Title_is_empty_when_neither_h1_nor_head_title_present()
    {
        var content = HtmlToMarkdown.ExtractMainContent(
            "<html><body><article><p>No title.</p></article></body></html>");

        Assert.Equal(string.Empty, content.Title);
    }

    // ---------- Edge cases ----------

    [Fact]
    public void Empty_body_returns_empty_markdown_not_null()
    {
        // The contract: empty / malformed inputs are not errors; they
        // return an empty MainContent rather than throwing.
        var content = HtmlToMarkdown.ExtractMainContent(
            "<html><body></body></html>");

        Assert.NotNull(content);
        Assert.Equal(string.Empty, content.Markdown);
    }

    [Fact]
    public void Empty_string_input_returns_empty_main_content()
    {
        // The most degenerate case — completely empty string.
        var content = HtmlToMarkdown.ExtractMainContent(string.Empty);

        Assert.NotNull(content);
        Assert.Equal(string.Empty, content.Markdown);
    }

    [Fact]
    public void Null_input_throws_argument_null_exception()
    {
        Assert.Throws<ArgumentNullException>(() =>
            HtmlToMarkdown.ExtractMainContent(null!));
    }

    [Fact]
    public void Whitespace_is_normalised_to_no_run_of_three_or_more_newlines()
    {
        // Multiple block separators collapse to exactly one blank line.
        var content = HtmlToMarkdown.ExtractMainContent(
            "<article><p>A.</p><p>B.</p><p>C.</p><p>D.</p></article>");

        Assert.DoesNotContain("\n\n\n", content.Markdown);
        // No leading or trailing blank lines either.
        Assert.DoesNotMatch(@"^\s*\n", content.Markdown);
        Assert.DoesNotMatch(@"\n\s*$", content.Markdown);
    }

    [Fact]
    public void Anchor_without_href_keeps_text_no_empty_link()
    {
        // A bare <a> with no href is not a meaningful Markdown link;
        // emit the text only.
        var content = HtmlToMarkdown.ExtractMainContent(
            "<article><p>Plain <a>anchor</a> text.</p></article>");

        Assert.Contains("Plain anchor text.", content.Markdown);
        Assert.DoesNotContain("[anchor]()", content.Markdown);
    }

    // ---------- The Convert overload ----------

    [Fact]
    public void Convert_returns_only_the_markdown_body_no_title()
    {
        // The Convert overload is the high-frequency caller's shape —
        // just the Markdown string, no MainContent wrapping.
        var html =
            "<html><head><title>Should not appear</title></head><body>" +
            "<article><h1>Hello</h1><p>Body.</p></article>" +
            "</body></html>";

        var md = HtmlToMarkdown.Convert(html);
        var full = HtmlToMarkdown.ExtractMainContent(html);

        Assert.Equal(full.Markdown, md);
        // The title is not in the returned string (it's an h1 inside,
        // not a prefixed title line).
        Assert.Contains("# Hello", md);
        Assert.DoesNotContain("Should not appear", md);
    }

    [Fact]
    public void Convert_is_pure_function_repeat_calls_return_identical_output()
    {
        // No hidden state — calling twice gives the same string.
        const string html = "<article><h1>X</h1><p>Stable.</p></article>";

        var first = HtmlToMarkdown.Convert(html);
        var second = HtmlToMarkdown.Convert(html);

        Assert.Equal(first, second);
    }
}
