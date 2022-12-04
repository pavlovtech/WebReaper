using AngleSharp;
using AngleSharp.Dom;
using Exoscan.Domain.Parsing;
using Exoscan.Parser.Abstract;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Exoscan.Parser.Concrete;

public class ContentParser : IContentParser
{
    private ILogger Logger { get; }

    public ContentParser(ILogger logger) => Logger = logger;

    public async Task<JObject> ParseAsync(string html, Schema schema) // TODO: consider passing url or headers... or http response
    {
        ArgumentNullException.ThrowIfNull(schema);
        
        var config = Configuration.Default.WithDefaultLoader();

        var context = BrowsingContext.New(config);
        
        // TODO temp fix
        using var doc = await context.OpenAsync(resp => resp.Header("Content-Type", "text/html; charset=utf-8").Content(html));

        return GetJson(doc, schema);
    }

    private JObject GetJson(IDocument doc, Schema schema)
    {
        var output = new JObject();

        foreach (var item in schema.Children)
        {
            FillOutput(output, doc, item);
        }

        return output;
    }

    private void FillOutput(JObject result, IDocument doc, SchemaElement item)
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