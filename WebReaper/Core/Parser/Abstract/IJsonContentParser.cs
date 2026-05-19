using System.Text.Json.Nodes;
using WebReaper.Domain.Parsing;

namespace WebReaper.Core.Parser.Abstract;

/// <summary>
/// The typed Schema-fold seam (ADR 0008). The fold's terminal projection is
/// <see cref="System.Text.Json.Nodes.JsonObject"/> — AOT-clean, no Newtonsoft —
/// and is the path <see cref="WebReaper.Sinks.Models.ParsedData"/>/sinks use. This is the sole
/// content-parser seam: the legacy Newtonsoft <c>JObject</c>-returning
/// <c>IContentParser</c> was removed outright at the 6.0.0 major — no compat
/// shell (see the ADR 0008 post-release correction). One fold (ADR 0002);
/// only what it emits differs.
/// </summary>
public interface IJsonContentParser
{
    /// <summary>
    /// Run the ADR-0002 Schema fold over <paramref name="content"/> and
    /// project the result to a <see cref="JsonObject"/> (the AOT-clean
    /// terminal, ADR-0008) — the content half of crawling a target page (link
    /// discovery is <see cref="ILinkParser"/>). A <c>null</c>
    /// <paramref name="schema"/> means no extraction (an empty object).
    /// </summary>
    Task<JsonObject> ParseToJsonAsync(string content, Schema? schema);
}
