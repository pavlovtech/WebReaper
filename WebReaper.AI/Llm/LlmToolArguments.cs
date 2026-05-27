using System.Text.Json;

namespace WebReaper.AI.Llm;

/// <summary>
/// Shared JSON-argument extractors used by every tool-calling <c>Llm*</c>
/// adapter when parsing <c>FunctionCallContent.Arguments</c> in
/// <see cref="LlmCallDescriptor{TResponse}.ParseToolCall"/>. Sibling to
/// <see cref="LlmCall{TResponse}"/> (ADR-0059) on the same "one canonical
/// mechanism, not five copies" axis — consumer-authored tool-calling
/// adapters reuse these helpers for consistent leniency rules instead of
/// re-implementing the same null-checks.
/// <para>
/// Both helpers tolerate two provider quirks: a missing property reads as
/// <c>null</c> (not a throw), and an integer serialised as a string (some
/// providers do this for small integers) parses as the integer. Other
/// kinds (arrays, booleans, malformed numbers) read as <c>null</c>.
/// </para>
/// </summary>
public static class LlmToolArguments
{
    /// <summary>Read a string-valued argument, returning <c>null</c> when
    /// the property is absent, JSON-null, or any non-string kind. Empty
    /// strings are returned verbatim — callers pattern-match on
    /// <c>{ Length: &gt; 0 }</c> when they need a non-empty selector.</summary>
    public static string? TryGetString(JsonElement args, string name)
    {
        if (args.ValueKind != JsonValueKind.Object) return null;
        if (!args.TryGetProperty(name, out var el)) return null;
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Null => null,
            _ => null,
        };
    }

    /// <summary>Read an integer-valued argument, returning <c>null</c>
    /// when the property is absent, JSON-null, or any non-integer kind.
    /// Tolerates the string-encoded-integer shape some providers emit
    /// for small numbers.</summary>
    public static int? TryGetInt(JsonElement args, string name)
    {
        if (args.ValueKind != JsonValueKind.Object) return null;
        if (!args.TryGetProperty(name, out var el)) return null;
        return el.ValueKind switch
        {
            JsonValueKind.Number when el.TryGetInt32(out var i) => i,
            // Some providers may serialise ints as strings; tolerate it.
            JsonValueKind.String when int.TryParse(el.GetString(), out var i) => i,
            _ => null,
        };
    }
}
