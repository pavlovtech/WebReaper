using Fizzler.Systems.HtmlAgilityPack;
using HtmlAgilityPack;

namespace Exoscan.Domain.Parsing;

public record SchemaElement()
{
    public string? Field { get; set; }
    public string? Selector { get; set; }
    public string? Attr { get; set; }
    public DataType? Type { get; set; }
    public bool GetHtml { get; set; }

    public SchemaElement(string field) : this() => Field = field;
 
    public SchemaElement(string field, string selector) : this(field) => Selector = selector;

    public SchemaElement(string field, string selector, DataType type) : this(field, selector) => Type = type;

    public SchemaElement(string field, string selector, string attr) : this(field, selector) => Attr = attr;

    public SchemaElement(string field, string selector, bool getHtml) : this(field, selector) => GetHtml = getHtml;

    public virtual string GetData(HtmlDocument doc)
    {
        var node = doc.DocumentNode.QuerySelector(Selector);

        if (node is null)
        {
            throw new InvalidOperationException($"Cannot find element by selector ${Selector}.");
        }

        string? content = null;

        if (Attr is not null)
        {
            content = node?.GetAttributeValue(Attr is not "src" ? Attr : "title", ""); // HTML Agility Pack workaround
        }
        else if (GetHtml == false)
        {
            content = node?.InnerText;
        }
        else
        {
            content = node?.InnerHtml;
        }

        return HtmlEntity.DeEntitize(content);
    }
}
