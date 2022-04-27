using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using WebReaper.Abstractions.Parsers;
using WebReaper.Domain.Parsing;

namespace WebReaper.Parser
{
    public class ContentParser : IContentParser
    {
        protected ILogger Logger { get; }

        public ContentParser(ILogger logger)
        {
            this.Logger = logger;

        }

        public JObject Parse(string html, Schema schema)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            return GetJson(doc, schema);
        }

        private JObject GetJson(HtmlDocument doc, Schema schema)
        {
            JObject output = new JObject();

            foreach (var item in schema.Children)
            {
                FillOutput(output, doc, item);
            }

            return output;
        }

        private void FillOutput(JObject result, HtmlDocument doc, Schema item)
        {
            if (item.IsComposite)
            {
                var obj = new JObject();

                foreach (var el in item.Children)
                {
                    FillOutput(obj, doc, el);
                }

                result[item.Field] = obj;

                return;
            }
           
            try
            {
                result[item.Field] = item.GetData(doc);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex.ToString());
            }
        }
    }
}