using System.Text.Json.Nodes;
using WebReaper.Domain.Parsing;

namespace WebReaper.Core.Parser.Abstract;

/// <summary>
/// The typed Schema-fold seam (ADR 0008). The fold's terminal projection is
/// <see cref="System.Text.Json.Nodes.JsonObject"/> — AOT-clean, no Newtonsoft —
/// and is the path <see cref="ParsedData"/>/sinks move to. It lives
/// <em>beside</em> the legacy <see cref="IContentParser"/> (<c>JObject</c>),
/// which is retained as a compat shell and <c>[Obsolete]</c>. One fold (ADR
/// 0002); only what it emits differs.
/// </summary>
public interface IJsonContentParser
{
    Task<JsonObject> ParseToJsonAsync(string content, Schema? schema);
}
