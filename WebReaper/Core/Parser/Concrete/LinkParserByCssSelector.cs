using AngleSharp;
using WebReaper.Core.Parser.Abstract;

namespace WebReaper.Core.Parser.Concrete;

public class LinkParserByCssSelector : ILinkParser
{
    public async Task<List<string>> GetLinksAsync(Uri baseUrl, string html, string cssSelector)
    {
        var config = Configuration.Default.WithDefaultLoader();
        var context = BrowsingContext.New(config);
        using var doc = await context.OpenAsync(req => req.Content(html));

        return doc
            .QuerySelectorAll(cssSelector)
            .Select(e => e.Attributes["href"]?.Value)
            .Select(l => new Uri(baseUrl, l).ToString())
            .Distinct()
            .ToList();
    }
}