namespace WebReaper.Core.Crawling.Concrete;

/// <summary>
/// The on-domain boundary a Site sweep (ADR-0081) follows when producing a
/// Sweep page's child Jobs. Default mode keeps links whose host equals the
/// anchor (start) host, treating a leading <c>www.</c> as the apex;
/// <c>includeSubdomains</c> widens to a suffix match on the apex host, a
/// documented heuristic, not public-suffix-list-correct (ADR-0081 rejected the
/// eTLD+1 dependency, against the ADR-0009 dependency-light-core bias). The
/// same-host check is the recursive sibling of the Site mapper's
/// <c>SameHost</c> (ADR-0042); the leading dot on the suffix match makes the
/// boundary a real DNS-label boundary, so <c>notexample.com</c> and
/// <c>example.com.evil.com</c> never count as on-domain for <c>example.com</c>.
/// </summary>
internal static class SweepDomainFilter
{
    /// <summary>
    /// True when <paramref name="url"/>'s host is on-domain for
    /// <paramref name="anchorHost"/>. A non-absolute or unparseable
    /// <paramref name="url"/> is off-domain (skipped, never thrown), mirroring
    /// the link-extractor's skip-the-unusable-href posture.
    /// </summary>
    public static bool IsOnDomain(string url, string anchorHost, bool includeSubdomains)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;

        var linkHost = uri.Host;
        var apex = StripWww(anchorHost);

        if (includeSubdomains)
        {
            return string.Equals(linkHost, apex, StringComparison.OrdinalIgnoreCase)
                || linkHost.EndsWith("." + apex, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(StripWww(linkHost), apex, StringComparison.OrdinalIgnoreCase);
    }

    private static string StripWww(string host) =>
        host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
}
