using Fizzler.Systems.HtmlAgilityPack;
using HtmlAgilityPack;
using Newtonsoft.Json.Linq;
using WebReaper.Domain;

namespace WebReaper.Parser.Concrete
{
    public class ContentParser : IContentParser
    {
        public JObject Parse(string html, SchemaElement[] schema)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            return GetJson(doc, schema);
        }

        private JObject GetJson(HtmlDocument doc, SchemaElement[] schema)
        {
            var output = new JObject();

            foreach (var item in schema)
            {
                var result = FillOutput(output, doc, item);
            }

            return output;
        }

        private HtmlNode QuerySelector(HtmlDocument doc, string selector)
        {
            return doc.DocumentNode.QuerySelector(selector);
        }

        private JObject FillOutput(JObject result, HtmlDocument doc, SchemaElement item)
        {
            var node = QuerySelector(doc, item.Selector);

            if(node == null) {
                throw new Exception($"No element found that matche the selector {item.Selector}.");
            }

            switch (item.Type)
            {
                case ContentType.String:
                    result[item.Field] = node?.InnerText;
                    break;
                case ContentType.Number:
                    result[item.Field] = int.TryParse(node?.InnerText, out var parsedInt) ? parsedInt : null;
                    break;
                case ContentType.Boolean:
                    result[item.Field] = bool.TryParse(node?.InnerText, out var parsedBool) ? parsedBool : null;
                    break;
                case ContentType.Image:
                    result[item.Field] = node?.GetAttributeValue("title", "");
                    break;
                case ContentType.Html:
                    result[item.Field] = node?.InnerHtml;
                    break;
                case ContentType.Url:
                    result[item.Field] = node?.GetAttributeValue("href", "");
                    break;
                    // case JsonType.Array: 
                    //     var arr = new JArray();
                    //     obj[item.Field] = arr;
                    //     foreach(var el in item.Children) {
                    //         var result = FillOutput(doc, el);
                    //         arr.Add(result);
                    //     }
                    //     break;
            }

            return result;
        }
    }
}