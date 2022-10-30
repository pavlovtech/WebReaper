using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using WebReaper.Domain.Parsing;
using WebReaper.Parser.Abstract;

namespace WebReaper.Parser.Concrete
{
    public class ContentParser : IContentParser
    {
        protected ILogger Logger { get; }

        public ContentParser(ILogger logger) => Logger = logger;

        public JObject Parse(string html, Schema? schema)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            return GetJson(doc, schema);
        }

        private JObject GetJson(HtmlDocument doc, Schema? schema)
        {
            JObject output = new JObject();

            foreach (var item in schema.Children)
            {
                FillOutput(output, doc, item);
            }

            return output;
        }

        private void FillOutput(JObject result, HtmlDocument doc, SchemaElement item)
        {
            if(item.Field is null)
            {
                throw new InvalidOperationException("Schema is invalid");
            }

            if (item is Schema container)
            {
                var obj = new JObject();

                foreach (var el in container.Children)
                {
                    FillOutput(obj, doc, el);
                }

                result[item.Field] = obj;

                return;
            }

            try
            {
                var data = item.GetData(doc);

                if (item.Type == null)
                {
                    result[item.Field] = data;

                    return;
                }

                switch (item.Type)
                {
                    case DataType.Integer:
                        result[item.Field] = int.Parse(data);
                        break;
                    case DataType.Boolean:
                        result[item.Field] = bool.Parse(data);
                        break;
                    case DataType.DataTime:
                        result[item.Field] = DateTime.Parse(data);
                        break;
                    case DataType.String:
                        result[item.Field] = data;
                        break;
                    case DataType.Float:
                        result[item.Field] = float.Parse(data);
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error during parsing phase");
                throw;
            }
        }
    }
}