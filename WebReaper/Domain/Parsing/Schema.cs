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

    /// <summary>Add a child field (the collection-initialiser entry point).</summary>
    public virtual void Add(SchemaElement element)
    {
        Children.Add(element);
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
