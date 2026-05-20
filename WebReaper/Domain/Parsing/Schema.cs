using System.Collections;

namespace WebReaper.Domain.Parsing;

/// <summary>
/// The extraction grammar for a target page: a <see cref="SchemaElement"/>
/// that also holds <see cref="Children"/>, so a schema is a tree of fields.
/// Implements <see cref="ICollection{T}"/> purely to enable collection-
/// initialiser syntax — <c>new Schema { new SchemaElement("title", "h1") }</c>
/// — over <see cref="Children"/>. The shared fold (ADR-0002) walks this tree;
/// a nested <see cref="Schema"/> child produces a sub-object.
/// </summary>
/// <remarks>
/// ADR-0028: <see cref="Add(SchemaElement)"/> enforces the Schema grammar at
/// the *construction* site — a child must have a non-empty
/// <see cref="SchemaElement.Field"/>; a leaf child must have a non-empty
/// <see cref="SchemaElement.Selector"/>; a child that is itself a Schema with
/// <see cref="SchemaElement.IsList"/> true must have a non-empty Selector
/// too (it locates the list-item scope). A nested non-list Schema is exempt
/// — it uses the parent scope and the fold never reads its own Selector.
/// Use <see cref="ListOf"/> to construct an object-list in one call.
/// </remarks>
/// <param name="Field">The output property name when this schema is itself a
/// nested field; null for the root.</param>
public record Schema(string? Field = null)
    : SchemaElement(Field), ICollection<SchemaElement>
{
    /// <summary>A root schema with no field name.</summary>
    public Schema() : this(Field: null)
    {
    }

    /// <summary>The child fields of this schema, in declaration order.</summary>
    public List<SchemaElement> Children { get; set; } = new();

    /// <summary>The number of direct <see cref="Children"/>.</summary>
    public int Count => Children.Count;

    /// <summary>Always false — a schema is mutable while being built.</summary>
    public bool IsReadOnly => false;

    /// <summary>
    /// Add a child field — the collection-initialiser entry point. ADR-0028:
    /// validates the Schema grammar at the add site: <c>element.Field</c> must
    /// be non-empty; if <paramref name="element"/> is a leaf (not a
    /// <see cref="Schema"/>), its <c>Selector</c> must be non-empty; if it is
    /// a <see cref="Schema"/> with <c>IsList = true</c>, its <c>Selector</c>
    /// must be non-empty (to locate the list-item scope). A nested non-list
    /// <see cref="Schema"/> is exempt — it uses the parent scope.
    /// </summary>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="element"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="element"/> violates the Schema grammar (missing Field,
    /// or missing Selector on a leaf / list container).</exception>
    public virtual void Add(SchemaElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        if (string.IsNullOrWhiteSpace(element.Field))
        {
            throw new ArgumentException(
                "Schema child must have a non-empty Field — every child is a named " +
                "output property. Construct via the positional ctor: " +
                "`new SchemaElement(\"name\", \".selector\")`.",
                nameof(element));
        }

        // The leaf path: must locate the value via a non-empty Selector.
        // The fold's per-leaf catch (SchemaContentParser) would swallow an
        // empty-selector leaf at parse time and leave the field unset
        // silently — ADR-0028 makes it a fast-fail at the add site.
        if (element is not Schema && string.IsNullOrWhiteSpace(element.Selector))
        {
            throw new ArgumentException(
                $"Leaf '{element.Field}' must have a non-empty Selector to locate " +
                "the value on the page. (A non-list nested object is the only " +
                "Schema element exempt from this rule — it nests using the parent's scope.)",
                nameof(element));
        }

        // The list-container path: must locate the list-item scope via a
        // non-empty Selector. A non-list Schema (nested object) is exempt.
        if (element is Schema container && container.IsList &&
            string.IsNullOrWhiteSpace(container.Selector))
        {
            throw new ArgumentException(
                $"List container '{container.Field}' (IsList = true) must have a " +
                "non-empty Selector to locate the list-item scope. Use " +
                "`Schema.ListOf(field, selector, ...)` to construct list-of-objects " +
                "in one call with the rule enforced.",
                nameof(element));
        }

        Children.Add(element);
    }

    /// <summary>
    /// Construct a list-of-objects Schema in one call: a nested
    /// <see cref="Schema"/> with <see cref="SchemaElement.IsList"/> set,
    /// the given <paramref name="selector"/> locating each list item, and
    /// <paramref name="children"/> evaluated relative to each match. ADR-0028:
    /// bundles the <c>IsList + Selector + Children</c> triple a user would
    /// otherwise have to remember to set together; validates
    /// <paramref name="field"/> and <paramref name="selector"/> non-empty at
    /// the factory call, and every child via <see cref="Add"/>.
    /// </summary>
    /// <param name="field">The output JSON property name for the resulting
    /// array.</param>
    /// <param name="selector">The selector locating each list-item scope
    /// (CSS / XPath / JSONPath, per the configured backend).</param>
    /// <param name="children">The sub-fields evaluated relative to each
    /// list-item scope.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="field"/> or <paramref name="selector"/> is
    /// null/empty/whitespace, or a child violates the Schema grammar.</exception>
    public static Schema ListOf(
        string field, string selector, params SchemaElement[] children)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        ArgumentException.ThrowIfNullOrWhiteSpace(selector);

        var schema = new Schema(field)
        {
            Selector = selector,
            IsList = true,
        };

        foreach (var child in children) schema.Add(child);

        return schema;
    }

    /// <summary>Remove all child fields.</summary>
    public void Clear()
    {
        Children.Clear();
    }

    /// <summary>Whether <paramref name="item"/> is a direct child.</summary>
    public bool Contains(SchemaElement item)
    {
        return Children.Contains(item);
    }

    /// <summary>Copy the child fields into <paramref name="array"/> starting
    /// at <paramref name="arrayIndex"/>.</summary>
    public void CopyTo(SchemaElement[] array, int arrayIndex)
    {
        Children.ToArray().CopyTo(array, arrayIndex);
    }

    /// <summary>Enumerate the child fields.</summary>
    public IEnumerator<SchemaElement> GetEnumerator()
    {
        return Children.GetEnumerator();
    }

    /// <summary>Remove a child field; returns whether it was present.</summary>
    public bool Remove(SchemaElement item)
    {
        return Children.Remove(item);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
