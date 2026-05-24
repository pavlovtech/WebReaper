using System.Text.Json.Nodes;
using WebReaper.Core.Parser.Abstract;
using WebReaper.Domain.Parsing;

namespace WebReaper.Core.Parser.Concrete;

/// <summary>
/// The default <see cref="ISchemaValidator"/> (ADR-0062, promoted from
/// the static helper that lived here before): a recursive walk over the
/// <see cref="Schema"/> testing each declared element against the
/// extractor's <see cref="JsonObject"/> output. Reports the result
/// <em>unsatisfied</em> when any required leaf is empty or absent.
/// <para>
/// Per-element rules (ADR-0029 alignment):
/// <list type="bullet">
/// <item>Leaf, non-list: present <em>and</em> (if string-typed) non-empty.
/// Integer 0 and boolean false count as valid data — only string-empty
/// triggers, since the fold writes empty string when the selector matched
/// nothing.</item>
/// <item>Leaf, IsList: present <em>and</em> non-empty array.</item>
/// <item>Container (Schema), non-list: present.</item>
/// <item>Container (Schema), IsList: present and non-empty array.</item>
/// </list>
/// A null <see cref="Schema"/> is trivially satisfied — no elements to
/// check.
/// </para>
/// </summary>
public sealed class SchemaSatisfiedValidator : ISchemaValidator
{
    /// <summary>
    /// The canonical singleton — the validator is stateless, so one
    /// instance is the right default for both builder docks (router,
    /// self-heal) and the agent driver.
    /// </summary>
    public static SchemaSatisfiedValidator Instance { get; } = new();

    /// <inheritdoc/>
    public ValidationResult Validate(JsonObject? extracted, Schema? schema)
    {
        // A null schema (Markdown / LLM strategy) or a null record is
        // trivially valid — there is nothing to check.
        if (schema is null || extracted is null) return ValidationResult.Valid;

        var failures = new List<string>();
        foreach (var child in schema.Children)
        {
            CheckElement(extracted, child, path: string.Empty, failures);
        }

        return failures.Count == 0
            ? ValidationResult.Valid
            : ValidationResult.Invalid(BuildReason(failures));
    }

    /// <summary>
    /// Backward-compatible static form for callers from earlier release
    /// cycles. Prefer the instance method via the seam; this delegates
    /// to <see cref="Instance"/> and discards the <see cref="ValidationResult.Reason"/>.
    /// Removed in v11.
    /// </summary>
    [Obsolete("Use ISchemaValidator.Validate via WithSchemaValidator. Removed in v11.")]
    public static bool IsSatisfied(JsonObject result, Schema? schema)
        => Instance.Validate(result, schema).IsValid;

    // Recursive walk: every failure path is accumulated into `failures`
    // instead of short-circuiting on the first one, so the resulting
    // Reason can name every field that needs repair (the self-heal
    // repairer reads it; the agent brain reads it; the router only
    // reads IsValid).
    private static void CheckElement(JsonObject parent, SchemaElement element, string path, List<string> failures)
    {
        var field = element.Field;
        if (field is null) return;

        var qualified = string.IsNullOrEmpty(path) ? field : $"{path}.{field}";

        if (!parent.TryGetPropertyValue(field, out var node) || node is null)
        {
            failures.Add(qualified);
            return;
        }

        // Containers — including IsList containers (a list of objects).
        if (element is Schema container)
        {
            if (container.IsList)
            {
                if (node is not JsonArray array || array.Count == 0)
                {
                    failures.Add(qualified);
                }
                return;
            }

            // A nested non-list Schema — presence is sufficient. The
            // walker doesn't descend into children for the default
            // policy: ADR-0029's "every required leaf non-empty" is
            // top-level only by precedent; a stricter policy is a
            // consumer-authored validator.
            return;
        }

        // Leaf, IsList = true.
        if (element.IsList)
        {
            if (node is not JsonArray arr || arr.Count == 0)
            {
                failures.Add(qualified);
            }
            return;
        }

        // Leaf, non-list. A string-typed leaf with empty value counts as
        // missing — the fold writes empty string when the selector matched
        // nothing (ADR-0029). Other types: presence is sufficient — a
        // legitimate "0" or "false" is valid data.
        if (node is JsonValue value)
        {
            if (element.Type is null || element.Type == DataType.String)
            {
                var s = value.TryGetValue<string>(out var str) ? str : null;
                if (string.IsNullOrEmpty(s)) failures.Add(qualified);
            }
        }
    }

    private static string BuildReason(List<string> failures)
        => failures.Count == 1
            ? $"required field '{failures[0]}' is empty"
            : "required fields are empty: " + string.Join(", ", failures.Select(f => $"'{f}'"));
}
