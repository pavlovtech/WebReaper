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

        var foundBySelector = doc.QuerySelectorAll(cssSelector);
        
        return foundBySelector
            .Select(e =>
            {
                var x = e.Attributes["href"]?.Value;
                return x;
            })
            .Select(l =>
            {
                var url = new Uri(baseUrl, l);

                return url.ToString();
            })
            .Distinct()
            .ToList();
    }
}