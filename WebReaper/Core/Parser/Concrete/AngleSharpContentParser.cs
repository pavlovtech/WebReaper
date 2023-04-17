using AngleSharp;
using AngleSharp.Dom;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using WebReaper.Core.Parser.Abstract;
using WebReaper.Domain.Parsing;

namespace WebReaper.Core.Parser.Concrete;

public class AngleSharpContentParser : IContentParser
{
    public AngleSharpContentParser(ILogger logger)
    {
        Logger = logger;
    }

    private ILogger Logger { get; }

    public async Task<JObject> ParseAsync(string html, Schema schema) // TODO: consider passing url or headers... or http response
    {
        ArgumentNullException.ThrowIfNull(schema);

        var config = Configuration.Default.WithDefaultLoader();

        var context = BrowsingContext.New(config);

        // TODO temp fix
        using var doc =
            await context.OpenAsync(resp => resp.Header("Content-Type", "text/html; charset=utf-8").Content(html));

        return GetJson(doc, schema);
    }

    private JObject GetJson(IDocument doc, Schema schema)
    {
        var output = new JObject();

        foreach (var item in schema.Children) FillOutput(output, doc, item);

        return output;
    }

    private void FillOutput(JObject result, IDocument doc, SchemaElement item)
    {
        if (item.Field is null) throw new InvalidOperationException("Schema is invalid");

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
            var data = GetData(doc, item);

            result[item.Field] = item.Type is null ? data : GetTypedValue(item, data);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error during parsing phase");
        }
    }

    private JToken GetTypedValue(SchemaElement item, string data) => item.Type switch
    {
        DataType.Integer => int.Parse(data),
        DataType.Boolean => bool.Parse(data),
        DataType.DataTime => DateTime.Parse(data),
        DataType.String => data,
        DataType.Float => float.Parse(data),
        _ => data
    };

    private string GetData(IDocument doc, SchemaElement el)
    {
        var node = doc.QuerySelector(el.Selector);

        if (node is null)
        {
            Logger.LogError(
                "Cannot find element by selector {selector}. Corresponding field will be empty in the result",
                el.Selector);
            
            return string.Empty;
        }

        var content = string.Empty;

        if (el.Attr is not null)
        {
            if (el.Attr == "src") el.Attr = "title";

            content = node.GetAttribute(el.Attr);
        }
        else if (el.GetHtml == false)
        {
            content = node.Text();
        }
        else
        {
            content = node.InnerHtml;
        }

        return content;
    }
}