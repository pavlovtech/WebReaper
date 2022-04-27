using HtmlAgilityPack;

namespace WebReaper.Domain.Schema;

public record CompositeSchemaElement : SchemaElement
{
    public SchemaElement[]? Children { get; set; }

    public CompositeSchemaElement(
        string field,
        params SchemaElement[] children): base(field)
    {
        Children = children;
    }

    public override string GetData(HtmlDocument htmlDocument)
    {
        throw new NotImplementedException();
    }
}