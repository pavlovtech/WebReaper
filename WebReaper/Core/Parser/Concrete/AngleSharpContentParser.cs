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

    // scope is the node selectors are evaluated against: the document at
    // the top level, or a single list-item element when recursing into a
    // list of objects (issue #28).
    private void FillOutput(JObject result, IParentNode scope, SchemaElement item)
    {
        if (item.Field is null) throw new InvalidOperationException("Schema is invalid");

        if (item is Schema container)
        {
            if (container.IsList)
            {
                result[item.Field] = GetObjectList(scope, container);
            }
            else
            {
                var obj = new JObject();

                foreach (var el in container.Children)
                {
                    FillOutput(obj, scope, el);
                }

                result[item.Field] = obj;
            }

            return;
        }

        try
        {
            result[item.Field] = item.IsList ? GetValueList(scope, item) : GetSingleValue(scope, item);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error during parsing phase");
        }
    }

    private JArray GetObjectList(IParentNode scope, Schema container)
    {
        RequireSelector(container);

        var array = new JArray();

        foreach (var element in scope.QuerySelectorAll(container.Selector))
        {
            var obj = new JObject();

            foreach (var child in container.Children)
            {
                FillOutput(obj, element, child);
            }

            array.Add(obj);
        }

        return array;
    }

    private JArray GetValueList(IParentNode scope, SchemaElement item)
    {
        RequireSelector(item);

        var array = new JArray();

        foreach (var node in scope.QuerySelectorAll(item.Selector))
        {
            var raw = ExtractValue(node, item);
            array.Add(item.Type is null ? raw : GetTypedValue(item, raw));
        }

        return array;
    }

    private JToken GetSingleValue(IParentNode scope, SchemaElement item)
    {
        var node = scope.QuerySelector(item.Selector);

        if (node is null)
        {
            Logger.LogError(
                "Cannot find element by selector {selector}. Corresponding field will be empty in the result",
                item.Selector);

            return string.Empty;
        }

        var data = ExtractValue(node, item);

        return item.Type is null ? data : GetTypedValue(item, data);
    }

    private static void RequireSelector(SchemaElement item)
    {
        if (string.IsNullOrEmpty(item.Selector))
        {
            throw new InvalidOperationException(
                $"Schema element '{item.Field}' has IsList = true but no selector.");
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

    private static string ExtractValue(IElement node, SchemaElement el)
    {
        string? content;

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

        return content ?? string.Empty;
    }
}
