using WebReaper.Domain.Parsing;
using WebReaper.Sinks.Models;

namespace WebReaper.Processing;

/// <summary>
/// Everything an <see cref="WebReaper.Processing.Abstract.IPageProcessor"/>
/// sees about one crawled target page (ADR-0038): the working extracted record
/// and the page it came from. The Crawl driver builds one per target page;
/// processors never construct it. Supersedes the <c>Metadata</c> handed to the
/// removed <c>PostProcess</c> callback — it carries the working
/// <see cref="ParsedData"/> (not a bare <c>JsonObject</c>) and adds the parsing
/// <see cref="Schema"/>, so an AI selector-repair processor can see what the
/// deterministic fold was told to extract.
/// </summary>
/// <param name="Data">The working record from the previous pipeline stage
/// (stage 0 sees the Schema fold's output). <see cref="ParsedData.Url"/> is the
/// page URL and it is also folded into <c>Data["url"]</c> (ADR-0031).</param>
/// <param name="Html">The raw page body as loaded — the input an AI
/// re-extraction or a confidence score reads.</param>
/// <param name="BackLinks">Ancestor URLs that led to this page, oldest
/// first.</param>
/// <param name="Schema">The extraction Schema the deterministic fold ran for
/// this page, or <c>null</c> for a crawl that extracts nothing. Read-only
/// context — a processor that learns a better selector returns a corrected
/// record; it does not rewrite the live crawl grammar.</param>
public sealed record PageContext(
    ParsedData Data,
    string Html,
    IReadOnlyList<string> BackLinks,
    Schema? Schema);
