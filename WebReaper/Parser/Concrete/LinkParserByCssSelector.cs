using Fizzler.Systems.HtmlAgilityPack;
using HtmlAgilityPack;
using WebReaper.Parser.Abstract;

namespace WebReaper.Parser.Concrete
{
    public class LinkParserByCssSelector : ILinkParser
    {
        public IEnumerable<string> GetLinks(Uri baseUrl, string html, string cssSelector)
        {
            var htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(html);
            return htmlDoc.DocumentNode
                .QuerySelectorAll(cssSelector)
                .Select(e => HtmlEntity.DeEntitize(e.GetAttributeValue("href", null)))
                .Select(l => new Uri(baseUrl, l).ToString())
                .Distinct();
        }
    }
}
