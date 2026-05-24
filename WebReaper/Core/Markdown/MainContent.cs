namespace WebReaper.Core.Markdown;

/// <summary>
/// The two-field result of <see cref="HtmlToMarkdown.ExtractMainContent"/>
/// (ADR-0063): the resolved title (post-strip) and the GFM-rendered
/// Markdown body of the main-content area. Pure-data; no behaviour.
/// </summary>
/// <param name="Title">
/// The first <c>&lt;h1&gt;</c> inside the chosen main-content root after
/// stripping non-content descendants; falls back to <c>&lt;head&gt;&lt;title&gt;</c>
/// when no <c>&lt;h1&gt;</c> survives. Empty string when neither is present.
/// </param>
/// <param name="Markdown">
/// The GFM rendering of the surviving DOM. Empty string when the document
/// has no parseable body.
/// </param>
public sealed record MainContent(string Title, string Markdown);
