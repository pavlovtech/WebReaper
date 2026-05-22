using System.Text;
using System.Text.Json.Nodes;
using AngleSharp;
using AngleSharp.Dom;
using WebReaper.Core.Parser.Abstract;
using WebReaper.Domain.Parsing;

namespace WebReaper.Core.Parser.Concrete;

/// <summary>
/// The Markdown adapter of the <see cref="IContentExtractor"/> seam
/// (ADR-0040): turn a loaded page into LLM-ready Markdown without a
/// <see cref="Schema"/>. Reached via the second
/// <see cref="WebReaper.Builders.ICrawlSeed"/> terminal
/// <c>AsMarkdown()</c>, this is the funnel's no-schema wedge — the
/// smallest possible call returns LLM-ready text.
/// <para>
/// Deterministic, AOT-clean, no LLM dependency. Output is a
/// <see cref="JsonObject"/> with two fields:
/// <c>title</c> (the first <c>&lt;h1&gt;</c> in the main content, else
/// <c>&lt;title&gt;</c>) and <c>markdown</c> (GFM-flavoured rendering of
/// the main content area). ADR-0031 folds the page URL in under
/// <c>"url"</c> at <c>ParsedData</c> construction — this extractor never
/// touches that key.
/// </para>
/// <para>
/// Main-content selection is a tag-based heuristic: <c>&lt;article&gt;</c>
/// → <c>&lt;main&gt;</c> → <c>[role=main]</c> → <c>&lt;body&gt;</c>; the
/// first one present wins. Non-content descendants (nav, aside, footer,
/// header, form, script, style, etc.) are stripped before walking.
/// Mozilla-Readability-style scoring is deliberately out of scope for v1
/// — see ADR-0040 Considered options (g).
/// </para>
/// <para>
/// The <see cref="Schema"/> parameter is accepted and ignored — the
/// Markdown strategy has no place to project a Schema onto. The
/// strategy-local schema requirement is documented on the seam
/// (ADR-0040 §3).
/// </para>
/// </summary>
public sealed class MarkdownContentExtractor : IContentExtractor
{
    // Non-content descendants stripped before rendering. Block-level
    // structural noise — navigation, ads, footers, scripts, forms —
    // typically wrapping the real content even inside the chosen
    // main-content node. The first three (script/style/template) and
    // anything aria-hidden are mandatory for correctness; the rest are
    // the Readability heuristic equivalent. Tag names are lowercased by
    // AngleSharp; selectors compose them as a single QueryAll call.
    private const string StripSelector =
        "script,style,noscript,template,iframe," +
        "nav,aside,footer,header,form,button,dialog," +
        "[role=navigation],[role=banner],[role=contentinfo]," +
        "[aria-hidden=true],[hidden]";

    /// <inheritdoc/>
    public async Task<JsonObject> ExtractAsync(string document, Schema? schema)
    {
        // ADR-0040: the Schema is strategy-locally ignored — the Markdown
        // extractor has no use for it. The seam's nullability already
        // advertised this variation (ADR-0039); the doc widened.
        _ = schema;

        var doc = await OpenAsync(document);

        try
        {
            var (headTitle, root) = SelectMainContent(doc);

            StripNonContent(root);

            // Title preference: the first surviving <h1> inside main
            // content, else <head><title>. Reading h1 post-strip prevents
            // a "Skip to content" hidden h1 in stripped chrome from
            // winning.
            var title = ResolveTitleAfterStrip(headTitle, root);

            var output = new StringBuilder();
            RenderBlock(root, output, new RenderState());

            var markdown = NormaliseWhitespace(output.ToString());

            return new JsonObject
            {
                ["title"] = JsonValue.Create(title),
                ["markdown"] = JsonValue.Create(markdown)
            };
        }
        finally
        {
            doc.Dispose();
        }
    }

    private static async Task<IDocument> OpenAsync(string content)
    {
        var config = Configuration.Default.WithDefaultLoader();
        var context = BrowsingContext.New(config);

        // Same construction the AngleSharp backends use — keeps charset
        // semantics consistent across CSS/XPath/Markdown extractors.
        return await context.OpenAsync(resp =>
            resp.Header("Content-Type", "text/html; charset=utf-8").Content(content));
    }

    private static (string Title, IElement Root) SelectMainContent(IDocument doc)
    {
        // <head><title> wins only if no <h1> exists inside the main
        // content; that swap happens at the end, after main is chosen.
        var headTitle = doc.QuerySelector("head > title")?.TextContent?.Trim() ?? string.Empty;

        // Tag-based Readability heuristic: <article> → <main> →
        // [role=main] → <body>. The first present wins. Cheap, explains
        // itself, ~95th-percentile correct on modern editorial pages.
        var root =
            doc.QuerySelector("article")
            ?? doc.QuerySelector("main")
            ?? doc.QuerySelector("[role=main]")
            ?? doc.Body
            // <body> may itself be null on a malformed document; fall back
            // to the document element so we never hand the walker null.
            ?? doc.DocumentElement;

        // Title preference: the first <h1> *inside* the chosen main
        // content, else <title>. An <h1> in nav/footer is filtered out
        // by the strip pass (which runs after this selection); reading
        // it pre-strip would let "Skip to content"-style hidden h1s win,
        // so we read post-strip below.
        return (headTitle, root!);
    }

    private static void StripNonContent(IElement root)
    {
        // QueryAll then remove — collected first because removing a
        // node mid-iteration mutates the same collection.
        var doomed = root.QuerySelectorAll(StripSelector).ToList();
        foreach (var element in doomed)
            element.Remove();
    }

    private static string ResolveTitleAfterStrip(string headTitle, IElement root)
    {
        var h1 = root.QuerySelector("h1");
        if (h1 is not null)
        {
            var text = h1.TextContent?.Trim();
            if (!string.IsNullOrEmpty(text)) return text;
        }
        return headTitle;
    }

    // The walker — block-level dispatch. Each block emits its own
    // separator before/after; the surrounding code never adds blank
    // lines manually. Block elements: hX, p, ul, ol, li, blockquote,
    // pre, hr, dl/dt/dd, table, and any container we don't know about
    // (handled as "walk children").
    private static void RenderBlock(INode node, StringBuilder output, RenderState state)
    {
        foreach (var child in node.ChildNodes)
        {
            switch (child)
            {
                case IText text:
                    // Stray top-level text outside a block: wrap in an
                    // implicit paragraph. Whitespace-only text is dropped.
                    var raw = text.TextContent;
                    if (!string.IsNullOrWhiteSpace(raw))
                    {
                        BreakBlock(output);
                        AppendInline(text, output, state);
                        BreakBlock(output);
                    }
                    break;

                case IElement element:
                    RenderElement(element, output, state);
                    break;

                default:
                    // Comments, processing instructions, etc — skip.
                    break;
            }
        }
    }

    private static void RenderElement(IElement element, StringBuilder output, RenderState state)
    {
        switch (element.TagName.ToLowerInvariant())
        {
            case "h1": EmitHeading(element, output, state, 1); break;
            case "h2": EmitHeading(element, output, state, 2); break;
            case "h3": EmitHeading(element, output, state, 3); break;
            case "h4": EmitHeading(element, output, state, 4); break;
            case "h5": EmitHeading(element, output, state, 5); break;
            case "h6": EmitHeading(element, output, state, 6); break;

            case "p":
                BreakBlock(output);
                AppendInline(element, output, state);
                BreakBlock(output);
                break;

            case "br":
                // A bare <br> at block level becomes a paragraph break.
                BreakBlock(output);
                break;

            case "hr":
                BreakBlock(output);
                output.Append("---");
                BreakBlock(output);
                break;

            case "ul":
                EmitList(element, output, state, ordered: false);
                break;

            case "ol":
                EmitList(element, output, state, ordered: true);
                break;

            case "blockquote":
                EmitBlockquote(element, output, state);
                break;

            case "pre":
                EmitCodeFence(element, output);
                break;

            case "table":
                EmitTable(element, output, state);
                break;

            // Containers we walk through transparently — they have no
            // block semantics in Markdown.
            case "div":
            case "section":
            case "article":
            case "main":
            case "figure":
            case "figcaption":
            case "details":
            case "summary":
            case "span" when ContainsBlock(element):
                RenderBlock(element, output, state);
                break;

            default:
                // Inline elements at block level: emit their text in an
                // implicit paragraph (covers <span>, <strong>, <em>, ...
                // that authors place outside <p>).
                if (ContainsBlock(element))
                {
                    RenderBlock(element, output, state);
                }
                else if (!string.IsNullOrWhiteSpace(element.TextContent))
                {
                    BreakBlock(output);
                    AppendInline(element, output, state);
                    BreakBlock(output);
                }
                break;
        }
    }

    private static void EmitHeading(IElement element, StringBuilder output, RenderState state, int level)
    {
        BreakBlock(output);
        output.Append('#', level).Append(' ');
        AppendInline(element, output, state);
        BreakBlock(output);
    }

    private static void EmitList(IElement element, StringBuilder output, RenderState state, bool ordered)
    {
        // GFM list rendering. Nested lists increase indent by two
        // spaces per level. Each <li> emits its own line; inline content
        // is rendered on the marker line, nested blocks (sublists,
        // paragraphs) on subsequent indented lines.
        BreakBlock(output);

        var counter = 1;
        foreach (var item in element.Children)
        {
            if (!item.TagName.Equals("li", StringComparison.OrdinalIgnoreCase)) continue;

            var indent = new string(' ', state.ListDepth * 2);
            output.Append(indent);
            if (ordered)
                output.Append(counter++.ToString()).Append(". ");
            else
                output.Append("- ");

            EmitListItem(item, output, state with { ListDepth = state.ListDepth + 1 });

            if (output.Length == 0 || output[^1] != '\n') output.Append('\n');
        }

        BreakBlock(output);
    }

    private static void EmitListItem(IElement li, StringBuilder output, RenderState childState)
    {
        // Inline content first (the marker line), then any block
        // children on indented lines. Mirrors how MD renderers expect
        // a list item to look.
        var inlineBuilder = new StringBuilder();
        var blocks = new List<IElement>();

        foreach (var child in li.ChildNodes)
        {
            switch (child)
            {
                case IText text when !string.IsNullOrWhiteSpace(text.TextContent):
                    inlineBuilder.Append(text.TextContent);
                    break;
                case IElement element when IsBlockElement(element):
                    blocks.Add(element);
                    break;
                case IElement element:
                    AppendInlineElement(element, inlineBuilder, childState);
                    break;
            }
        }

        output.Append(CollapseInlineSpaces(inlineBuilder.ToString()));

        if (blocks.Count > 0)
        {
            // Children of a list item are rendered indented; consumers
            // (and tooling) read the structure unambiguously.
            output.Append('\n');
            var nested = new StringBuilder();
            foreach (var block in blocks)
                RenderElement(block, nested, childState);
            var indent = new string(' ', childState.ListDepth * 2);
            foreach (var line in nested.ToString().Split('\n'))
            {
                if (string.IsNullOrEmpty(line)) { output.Append('\n'); continue; }
                output.Append(indent).Append(line).Append('\n');
            }
        }
    }

    private static void EmitBlockquote(IElement element, StringBuilder output, RenderState state)
    {
        BreakBlock(output);
        var inner = new StringBuilder();
        RenderBlock(element, inner, state with { QuoteDepth = state.QuoteDepth + 1 });
        var prefix = string.Concat(Enumerable.Repeat("> ", state.QuoteDepth + 1));
        foreach (var line in inner.ToString().TrimEnd('\n').Split('\n'))
        {
            if (string.IsNullOrEmpty(line)) output.Append(prefix.TrimEnd()).Append('\n');
            else output.Append(prefix).Append(line).Append('\n');
        }
        BreakBlock(output);
    }

    private static void EmitCodeFence(IElement pre, StringBuilder output)
    {
        // GFM fenced code. If the <pre> contains a <code class="language-X">,
        // emit the language hint on the opening fence.
        var code = pre.QuerySelector(":scope > code") ?? pre.FirstElementChild;
        var language = string.Empty;
        if (code is not null)
        {
            var className = code.GetAttribute("class") ?? string.Empty;
            foreach (var token in className.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                const string prefix = "language-";
                if (token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    language = token[prefix.Length..];
                    break;
                }
            }
        }

        var content = (code ?? pre).TextContent ?? string.Empty;

        BreakBlock(output);
        output.Append("```").Append(language).Append('\n');
        output.Append(content.TrimEnd('\n'));
        output.Append('\n').Append("```");
        BreakBlock(output);
    }

    private static void EmitTable(IElement table, StringBuilder output, RenderState state)
    {
        // GFM table — read THEAD then TBODY then TFOOT, normalising row
        // shape. If there is no THEAD, the first <tr> is treated as the
        // header (the common shape in scraped tables).
        var rows = new List<List<string>>();
        foreach (var row in table.QuerySelectorAll("tr"))
        {
            var cells = new List<string>();
            foreach (var cell in row.Children)
            {
                if (!cell.TagName.Equals("th", StringComparison.OrdinalIgnoreCase) &&
                    !cell.TagName.Equals("td", StringComparison.OrdinalIgnoreCase)) continue;
                var inline = new StringBuilder();
                AppendInline(cell, inline, state);
                // Newlines inside a cell break the GFM grammar; collapse.
                cells.Add(CollapseInlineSpaces(inline.ToString()).Replace('\n', ' ').Trim());
            }
            if (cells.Count > 0) rows.Add(cells);
        }

        if (rows.Count == 0) return;

        var columnCount = rows.Max(r => r.Count);
        // Pad short rows so the grammar's column count matches.
        foreach (var r in rows)
            while (r.Count < columnCount) r.Add(string.Empty);

        BreakBlock(output);
        var header = rows[0];
        output.Append("| ").Append(string.Join(" | ", header.Select(EscapeTableCell))).Append(" |\n");
        output.Append("| ").Append(string.Join(" | ", Enumerable.Repeat("---", columnCount))).Append(" |\n");
        for (int i = 1; i < rows.Count; i++)
            output.Append("| ").Append(string.Join(" | ", rows[i].Select(EscapeTableCell))).Append(" |\n");
        BreakBlock(output);
    }

    private static string EscapeTableCell(string s) => s.Replace("|", "\\|");

    // Inline rendering — collected from a node's children, no leading
    // or trailing block separators. Called from headings, paragraphs,
    // list-item markers, table cells, blockquote leaves.
    private static void AppendInline(INode node, StringBuilder output, RenderState state)
    {
        foreach (var child in node.ChildNodes)
        {
            switch (child)
            {
                case IText text:
                    output.Append(text.TextContent);
                    break;
                case IElement element:
                    AppendInlineElement(element, output, state);
                    break;
            }
        }
    }

    private static void AppendInlineElement(IElement element, StringBuilder output, RenderState state)
    {
        switch (element.TagName.ToLowerInvariant())
        {
            case "br":
                output.Append("  \n");
                break;

            case "a":
                var href = element.GetAttribute("href");
                var linkText = new StringBuilder();
                AppendInline(element, linkText, state);
                var textPart = CollapseInlineSpaces(linkText.ToString()).Trim();
                if (string.IsNullOrEmpty(href))
                {
                    output.Append(textPart);
                }
                else
                {
                    output.Append('[').Append(textPart).Append("](").Append(href).Append(')');
                }
                break;

            case "img":
                var src = element.GetAttribute("src");
                var alt = element.GetAttribute("alt") ?? string.Empty;
                if (!string.IsNullOrEmpty(src))
                    output.Append("![").Append(alt).Append("](").Append(src).Append(')');
                break;

            case "strong":
            case "b":
                output.Append("**");
                AppendInline(element, output, state);
                output.Append("**");
                break;

            case "em":
            case "i":
                output.Append('*');
                AppendInline(element, output, state);
                output.Append('*');
                break;

            case "code":
                output.Append('`').Append(element.TextContent).Append('`');
                break;

            case "del":
            case "s":
                output.Append("~~");
                AppendInline(element, output, state);
                output.Append("~~");
                break;

            // Block elements that landed in an inline context: walk
            // their inline content (best-effort — the HTML is malformed).
            default:
                AppendInline(element, output, state);
                break;
        }
    }

    private static bool ContainsBlock(IElement element)
    {
        foreach (var child in element.Children)
            if (IsBlockElement(child)) return true;
        return false;
    }

    private static bool IsBlockElement(IElement element)
    {
        return element.TagName.ToLowerInvariant() switch
        {
            "h1" or "h2" or "h3" or "h4" or "h5" or "h6" => true,
            "p" => true,
            "ul" or "ol" or "li" => true,
            "blockquote" => true,
            "pre" => true,
            "table" => true,
            "hr" => true,
            "div" or "section" or "article" or "main" or "header" or "footer" or "aside" or "nav" => true,
            "figure" or "figcaption" or "details" or "summary" => true,
            _ => false
        };
    }

    private static void BreakBlock(StringBuilder output)
    {
        // Idempotent block break: ensures the next block starts on a
        // line of its own, separated by at least one blank line. We
        // intentionally over-produce here and normalise at the end —
        // simpler than tracking pending breaks.
        if (output.Length == 0) return;
        if (output[^1] != '\n') output.Append('\n');
        if (output.Length < 2 || output[^2] != '\n') output.Append('\n');
    }

    private static string CollapseInlineSpaces(string text)
    {
        // HTML collapses runs of whitespace to a single space; we mirror.
        var output = new StringBuilder(text.Length);
        var inSpace = false;
        foreach (var c in text)
        {
            if (c == '\n' || c == '\r' || c == '\t' || c == ' ')
            {
                if (!inSpace) output.Append(' ');
                inSpace = true;
            }
            else
            {
                output.Append(c);
                inSpace = false;
            }
        }
        return output.ToString();
    }

    private static string NormaliseWhitespace(string markdown)
    {
        // Collapse any run of 3+ newlines to exactly 2 (one blank line
        // between blocks). Trim leading/trailing blank lines.
        var output = new StringBuilder(markdown.Length);
        var newlines = 0;
        foreach (var c in markdown)
        {
            if (c == '\r') continue;
            if (c == '\n')
            {
                newlines++;
                if (newlines <= 2) output.Append('\n');
            }
            else
            {
                newlines = 0;
                output.Append(c);
            }
        }
        return output.ToString().Trim('\n');
    }

    // The walker's only mutable carry-along: list-nest depth and
    // blockquote depth (both 0 at the document root). A struct so it
    // is allocation-free; copied on `with`.
    private readonly record struct RenderState(int ListDepth = 0, int QuoteDepth = 0);
}
