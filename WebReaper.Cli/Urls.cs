namespace WebReaper.Cli;

// Bare-host ergonomics for the CLI. `webreaper scrape alexpavlov.dev` should
// Just Work; the library's Crawl / MapAsync require an absolute URI (an
// HttpClient handed "alexpavlov.dev" throws "An invalid request URI was
// provided"), so the CLI defaults a missing scheme to https:// at the arg
// boundary. Kept out of the library on purpose: Crawl(string) is an API whose
// absolute-URI contract is reasonable; this is a typed-at-a-terminal nicety.
internal static class Urls
{
    /// <summary>
    /// Normalize a user-typed URL. A bare host ("example.com",
    /// "example.com/path") gets an https:// scheme; an explicit scheme
    /// ("http://", "https://", or any "scheme://") is left untouched, as is a
    /// protocol-relative "//host" (defaulted to https:). Whitespace is trimmed.
    /// </summary>
    public static string Normalize(string raw)
    {
        var url = raw.Trim();
        if (url.Length == 0) return url;                                   // let downstream report the empty
        if (url.Contains("://", StringComparison.Ordinal)) return url;      // explicit scheme
        if (url.StartsWith("//", StringComparison.Ordinal)) return "https:" + url; // protocol-relative
        return "https://" + url;
    }
}
