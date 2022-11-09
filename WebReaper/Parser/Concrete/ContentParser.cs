using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using WebReaper.Domain.Parsing;
using WebReaper.Parser.Abstract;

namespace WebReaper.Parser.Concrete;

public class ContentParser : IContentParser
{
    private ILogger Logger { get; }

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

            if (item.Type is null)
            {
                result[item.Field] = data;

                return;
            }

            result[item.Field] = item.Type switch
            {
                DataType.Integer => int.Parse(data),
                DataType.Boolean => bool.Parse(data),
                DataType.DataTime => DateTime.Parse(data),
                DataType.String => data,
                DataType.Float => float.Parse(data),
                _ => result[item.Field]
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error during parsing phase");
        }
    }
}