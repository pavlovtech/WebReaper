using HtmlAgilityPack;
using Newtonsoft.Json.Linq;
using WebReaper.Abstractions.Parsers;
using WebReaper.Domain.Schema;

namespace WebReaper.Parser
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

        private JObject FillOutput(JObject result, HtmlDocument doc, SchemaElement item)
        {
            if (item is CompositeSchemaElement composite) {
                var obj = new JObject();
                    
                foreach(var el in composite.Children) {
                   var child = FillOutput(result, doc, el);
                }
                result[item.Field] = obj;
               
            } else {
                 result[item.Field] = item.GetData(doc);
            }

            return result;
        }
    }
}