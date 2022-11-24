using System.Collections.Immutable;
using Exoscan.Domain.Parsing;
using Exoscan.Domain.Selectors;
using Exoscan.PageActions;
using Newtonsoft.Json;

namespace Exoscan.ConfigStorage;

public record ScraperConfig(
    Schema? ParsingScheme,
    ImmutableQueue<LinkPathSelector> LinkPathSelectors,
    string StartUrl,
    PageType StartPageType = PageType.Static,
    List<PageAction>? PageActions = null
)
{
    public string ToJson()
    {
        return SerializeToJson();
    }
    
    private string SerializeToJson()
    {
        var json = JsonConvert.SerializeObject(this, Formatting.Indented, new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.Auto
        });

        return json;
    }
};
