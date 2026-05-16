using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using WebReaper.Core.Parser.Abstract;
using WebReaper.Domain.Parsing;

namespace WebReaper.Core.Parser.Concrete;

/// <summary>
/// Parses a JSON response instead of HTML (issue #27 — scraping JSON
/// endpoints such as the WordPress REST API). Each
/// <see cref="SchemaElement.Selector"/> is a JSONPath expression
/// evaluated with Newtonsoft's <c>SelectToken</c>/<c>SelectTokens</c>,
/// relative to the current scope. <see cref="SchemaElement.IsList"/>
/// works the same way it does for the HTML parser.
/// </summary>
public class JsonContentParser : IContentParser
{
    public JsonContentParser(ILogger logger) => Logger = logger;

    private ILogger Logger { get; }

    public Task<JObject> ParseAsync(string json, Schema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        var root = JToken.Parse(json);
        var output = new JObject();

        foreach (var item in schema.Children)
        {
            FillOutput(output, root, item);
        }

        return Task.FromResult(output);
    }

    private void FillOutput(JObject result, JToken scope, SchemaElement item)
    {
        if (item.Field is null) throw new InvalidOperationException("Schema is invalid");

        if (item is Schema container)
        {
            if (container.IsList)
            {
                var array = new JArray();

                foreach (var token in SelectMany(scope, container))
                {
                    var obj = new JObject();
                    foreach (var child in container.Children)
                    {
                        FillOutput(obj, token, child);
                    }
                    array.Add(obj);
                }

                result[item.Field] = array;
            }
            else
            {
                var obj = new JObject();
                foreach (var child in container.Children)
                {
                    FillOutput(obj, scope, child);
                }
                result[item.Field] = obj;
            }

            return;
        }

        try
        {
            if (item.IsList)
            {
                var array = new JArray();
                foreach (var token in SelectMany(scope, item))
                {
                    array.Add(Convert(item, token));
                }
                result[item.Field] = array;
            }
            else
            {
                var token = scope.SelectToken(RequireSelector(item));

                if (token is null)
                {
                    Logger.LogError(
                        "JSONPath {selector} matched nothing. Field {field} will be empty",
                        item.Selector, item.Field);
                    result[item.Field] = string.Empty;
                    return;
                }

                result[item.Field] = Convert(item, token);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error during JSON parsing phase");
        }
    }

    private static IEnumerable<JToken> SelectMany(JToken scope, SchemaElement item)
        => scope.SelectTokens(RequireSelector(item));

    private static string RequireSelector(SchemaElement item)
    {
        if (string.IsNullOrEmpty(item.Selector))
        {
            throw new InvalidOperationException(
                $"Schema element '{item.Field}' has no JSONPath selector.");
        }

        return item.Selector;
    }

    // With an explicit Type, coerce via string (consistent with the HTML
    // parser). Otherwise keep the native JSON value (number stays number,
    // bool stays bool) instead of stringifying it.
    private JToken Convert(SchemaElement item, JToken token) => item.Type switch
    {
        DataType.Integer => int.Parse(token.ToString()),
        DataType.Boolean => bool.Parse(token.ToString()),
        DataType.DataTime => DateTime.Parse(token.ToString()),
        DataType.Float => float.Parse(token.ToString()),
        DataType.String => token.ToString(),
        _ => token.DeepClone()
    };
}
