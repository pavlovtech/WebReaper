using System.Text.Json.Nodes;
using WebReaper.Core.Parser.Abstract;
using WebReaper.Domain.Parsing;

namespace WebReaper.Core.Parser.Concrete;

/// <summary>
/// JSON backend for <see cref="SchemaContentParser{TNode}"/> (issue #27 —
/// scraping JSON endpoints such as the WordPress REST API). The scope cursor
/// is a <see cref="JsonNode"/> and each <see cref="SchemaElement.Selector"/>
/// is a JSONPath expression.
/// <para>
/// ADR 0008 named follow-up (now closed): this backend's JSONPath cursor was
/// the last Newtonsoft <c>JToken</c> reach in core — System.Text.Json has no
/// JSONPath — and gated a zero-warning whole-core <c>PublishAot</c>. It is now
/// an in-repo JSONPath-subset evaluator over <see cref="JsonNode"/>: pure
/// reflection-free traversal, zero dependency, AOT-clean by construction
/// (proven by <c>WebReaper.AotSmokeTest</c>). The supported dialect is exactly
/// what the <see cref="Schema"/> model drives — verified against the whole
/// JSON test corpus — an optional <c>$</c>/<c>$.</c> root anchor,
/// <c>.</c>-separated property segments, and an optional trailing <c>[*]</c>
/// array wildcard per segment. No indices, recursion, filters, slices or
/// unions: Newtonsoft's <c>SelectToken</c>/<c>SelectTokens</c> supported them
/// but nothing in WebReaper used them (ADR 0002: a backend's document
/// mechanics are backend-local quirks; the contract is the pinned dialect).
/// <see cref="ExtractRaw"/> returns the matched node deep-cloned so the fold
/// can graft it into the output tree; the clone preserves JSON value kind (a
/// number stays a number) — the structured side of the ADR-0002 untyped-leaf
/// divergence, previously carried by a Newtonsoft→JsonNode bridge that lived
/// here and is now deleted (the node is already a <see cref="JsonNode"/>).
/// </para>
/// </summary>
internal sealed class JsonSchemaBackend : ISchemaBackend<JsonNode>
{
    public Task<JsonNode> RootAsync(string content)
        => Task.FromResult(JsonNode.Parse(content)
            ?? throw new InvalidOperationException("JSON document parsed to null"));

    public IEnumerable<JsonNode> SelectMany(JsonNode scope, string selector)
        => Eval(scope, selector);

    public JsonNode? SelectOne(JsonNode scope, string selector)
        => Eval(scope, selector).FirstOrDefault();

    // Detached from the parsed tree: a parented JsonNode cannot be re-added,
    // and the fold grafts this into the output JsonObject/JsonArray. DeepClone
    // preserves the JSON value kind (number/bool stay native) — the ADR-0002
    // untyped-leaf divergence, carried natively now instead of via a bridge.
    public object? ExtractRaw(JsonNode node, SchemaElement element)
        => node.DeepClone();

    private static IEnumerable<JsonNode> Eval(JsonNode? scope, string selector)
    {
        if (scope is null) yield break;

        IEnumerable<JsonNode?> current = new JsonNode?[] { scope };

        foreach (var (name, wildcard) in Segments(selector))
            current = Step(current, name, wildcard);

        foreach (var node in current)
            if (node is not null) yield return node;
    }

    private static IEnumerable<JsonNode?> Step(
        IEnumerable<JsonNode?> nodes, string? name, bool wildcard)
    {
        foreach (var node in nodes)
        {
            if (node is null) continue;

            // Property step, unless the segment is a bare "[*]" (name null).
            var scoped = name is null
                ? node
                : node is JsonObject obj && obj.TryGetPropertyValue(name, out var v)
                    ? v
                    : null;

            if (scoped is null) continue;

            if (wildcard)
            {
                if (scoped is JsonArray array)
                    foreach (var element in array)
                        yield return element;
            }
            else
            {
                yield return scoped;
            }
        }
    }

    private static IEnumerable<(string? Name, bool Wildcard)> Segments(string selector)
    {
        var s = selector.Trim();
        if (s.StartsWith('$')) s = s[1..];
        s = s.TrimStart('.');
        if (s.Length == 0) yield break;

        foreach (var raw in s.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var part = raw;
            var wildcard = part.EndsWith("[*]", StringComparison.Ordinal);
            if (wildcard) part = part[..^3];
            yield return (part.Length == 0 ? null : part, wildcard);
        }
    }
}
