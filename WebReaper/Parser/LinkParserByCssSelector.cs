using Fizzler.Systems.HtmlAgilityPack;
using HtmlAgilityPack;
using WebReaper.Abstractions.Parsers;

namespace WebReaper.Parser
{
    public class LinkParserByCssSelector : ILinkParser
    {
        public IEnumerable<string> GetLinks(string html, string cssSelector)
        {
            var htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(html);
            return htmlDoc.DocumentNode
                .QuerySelectorAll(cssSelector)
                .Select(e => HtmlEntity.DeEntitize(e.GetAttributeValue("href", null)))
                .Distinct();
        }
    }
}
