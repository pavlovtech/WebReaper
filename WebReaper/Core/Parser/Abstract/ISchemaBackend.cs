using WebReaper.Domain.Parsing;

namespace WebReaper.Core.Parser.Abstract;

/// <summary>
/// One document shape = one backend. Supplies only the four
/// document-primitive operations the <see cref="Concrete.SchemaFold{TNode}"/>
/// fold needs; it does NOT know the <see cref="Schema"/> grammar
/// (container / object-list / leaf / value-list), type coercion, the
/// missing-node policy, or the swallow-and-log scope — those live once,
/// in the fold. A new backend (HtmlAgilityPack, System.Text.Json, …) is
/// an implementation of this seam, not a re-derivation of the walk.
/// </summary>
/// <typeparam name="TNode">
/// The backend's opaque scope cursor — an AngleSharp node, a
/// <c>JToken</c>, etc. The fold never names it; it only threads it back
/// through these methods.
/// </typeparam>
public interface ISchemaBackend<TNode> where TNode : class
{
    /// <summary>Parse a raw document string into its root scope.</summary>
    Task<TNode> RootAsync(string content);

    /// <summary>Every node under <paramref name="scope"/> matching the selector (list paths).</summary>
    IEnumerable<TNode> SelectMany(TNode scope, string selector);

    /// <summary>The first node under <paramref name="scope"/> matching the selector, or null if none.</summary>
    TNode? SelectOne(TNode scope, string selector);

    /// <summary>
    /// The raw leaf value of <paramref name="node"/> for <paramref name="element"/>:
    /// a <see cref="string"/> for text/markup backends, a native value for
    /// structured ones. The fold applies any <see cref="SchemaElement.Type"/>
    /// coercion; an untyped leaf is this value verbatim. Backend-local quirks
    /// (e.g. the AngleSharp <c>src</c>→<c>title</c> rewrite) belong here and
    /// nowhere else.
    /// </summary>
    object? ExtractRaw(TNode node, SchemaElement element);
}
