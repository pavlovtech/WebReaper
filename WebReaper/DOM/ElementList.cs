using Fizzler.Systems.HtmlAgilityPack;
using HtmlAgilityPack;
using System.Linq;
using WebReaper.Domain.Parsing;
using WebReaper.Domain.Selectors;

namespace WebReaper.Core.DOM
{
    public record ElementList(string Field, string Selector, SelectorType? SelectorType = SelectorType.Css)
    : SchemaElement(Field, Selector, SelectorType, DataType.List)
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
