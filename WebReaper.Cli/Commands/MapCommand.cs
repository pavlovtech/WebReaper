using WebReaper.Builders;
using WebReaper.Core.Mapping;

namespace WebReaper.Cli.Commands;

// `webreaper map <url>` — URL discovery via ADR-0042's ISiteMapper.
// One URL per line on stdout (or to --output).
internal static class MapCommand
{
    public static async Task<int> RunAsync(ParsedArgs args)
    {
        if (args.Positional.Count < 1)
            throw new CliException("Missing <url>. Usage: webreaper map <url> [flags]");

        var url = args.Positional[0];
        var options = new MapOptions(
            MaxUrls: args.GetIntFlag("max-urls", 1000),
            IncludeSitemap: !args.HasFlag("no-sitemap"),
            IncludeRootPageLinks: !args.HasFlag("no-root-page"),
            AllowOffsite: args.HasFlag("allow-offsite"),
            Search: args.GetFlag("search"));

        var urls = await ScraperEngineBuilder.MapAsync(url, options);

        var output = args.GetFlag("output");
        if (output is not null)
        {
            await File.WriteAllLinesAsync(output, urls);
        }
        else
        {
            foreach (var u in urls) Console.WriteLine(u);
        }

        return 0;
    }
}
