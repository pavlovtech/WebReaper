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
            HtmlNode? node = null;

            if(item.ContentType != ContentType.Nested) {
                ArgumentNullException.ThrowIfNull(item.Selector);

                node = QuerySelector(doc, item.Selector);

                if(node == null) {
                    throw new Exception($"No element found that matche the selector {item.Selector}.");
                }
            }

            bool ok = false;

            switch (item.ContentType)
            {
                case ContentType.String:
                    var str = node?.InnerText;

                    ok = !string.IsNullOrWhiteSpace(str);

                    if(ok) {
                        result[item.Field] = str;
                    } else {
                        throw new Exception($"Cannot find image link in {node?.OuterHtml}.");
                    }
                    break;
                case ContentType.Number:
                    ok = int.TryParse(node?.InnerText, out int parsedInt);

                    if(ok) {
                        result[item.Field] = parsedInt;
                    } else {
                        throw new Exception($"Cannot convert {node?.InnerText} to the integer type.");
                    }
                    break;
                case ContentType.Boolean:
                    ok = bool.TryParse(node?.InnerText, out bool parsedBool);

                    if(ok) {
                        result[item.Field] = parsedBool;
                    } else {
                        throw new Exception($"Cannot convert {node?.InnerText} to the integer type.");
                    }
                    break;
                case ContentType.Image:
                    var value = node?.GetAttributeValue("title", "");

                    ok = !string.IsNullOrWhiteSpace(value);

                    if(ok) {
                        result[item.Field] = value;
                    } else {
                        throw new Exception($"Cannot find image link in {node?.OuterHtml}.");
                    }
                    break;
                case ContentType.Html:
                    var html = node?.InnerHtml;

                    ok = !string.IsNullOrWhiteSpace(html);

                    if(ok) {
                        result[item.Field] = html;
                    } else {
                        throw new Exception($"No html found in convert {html}.");
                    }
                    break;
                case ContentType.Url:
                    var url = node?.GetAttributeValue("href", "");

                    ok = !string.IsNullOrWhiteSpace(url);

                    if(ok) {
                        result[item.Field] = url;
                    } else {
                        throw new Exception($"No href attribute found in {node}.");
                    }
                    break;
                case ContentType.Nested: 

                    if(item.Children == null) {
                        throw new Exception("ContentType is incorrect, node has no children");
                    }

                    var obj = new JObject();
                    
                    foreach(var el in item.Children) {
                        var child = FillOutput(obj, doc, el);
                    }
                    result[item.Field] = obj;
                    break;
            }

            return result;
        }
    }
}