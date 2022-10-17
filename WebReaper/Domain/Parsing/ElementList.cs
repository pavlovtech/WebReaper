using Fizzler.Systems.HtmlAgilityPack;
using HtmlAgilityPack;

namespace WebReaper.Domain.Parsing
{
    public record ElementList(string Field, string Selector)
    : SchemaElement(Field, Selector, DataType.List)
    {
        public override string GetData(HtmlDocument doc)
        {
            var nodes = doc.DocumentNode.QuerySelectorAll(Selector);

            if (!nodes.Any())
            {
                throw new InvalidOperationException($"No list items found by selector {Selector}.");
            }

            var content = string.Join("~", nodes.Select(el => el?.InnerText));

            return HtmlEntity.DeEntitize(content);
        }
    }

}
